// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;

namespace AbsurdSdk
{
    public class Absurd : IAbsurd, IDisposable, IAsyncDisposable
    {
        private readonly ILogger _logger;

        private readonly AbsurdDatabase _db = new AbsurdDatabase();
        private readonly NpgsqlDataSource _dataSource;        
        private readonly Dictionary<string, RegisteredTask> _registry;

        public Absurd(ILogger<Absurd> logger, NpgsqlDataSource dataSource)
        {
            _logger = logger;
            _dataSource = dataSource;
            _registry = new Dictionary<string, RegisteredTask>();
        }

        public void RegisterTask(TaskRegistrationOptions options, TaskHandler handler)
        {
            if (string.IsNullOrEmpty(options.Name))
            {
                throw new ArgumentException("Task registration requires a name");
            }

            _registry[options.Name] = new RegisteredTask
            {
                Name = options.Name,
                Queue = options.Queue,
                DefaultMaxAttempts = options.DefaultMaxAttempts,
                DefaultCancellation = options.DefaultCancellation,
                Handler = handler
            };
        }

        public async Task CreateQueue(string queueName)
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

            await _db.CreateQueue(conn, queueName).ConfigureAwait(false);
        }

        public async Task DropQueue(string queueName)
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

            await _db.DropQueue(conn, queueName).ConfigureAwait(false);
        }

        public async Task<IEnumerable<string>> ListQueues()
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
            return await _db.ListQueues(conn).ConfigureAwait(false);
        }

        public async Task<SpawnResult> Spawn(SpawnOptions options, string taskName, object parameters)
        {
            RegisteredTask? registration = null;

            _registry.TryGetValue(taskName, out registration);

            CancellationPolicy? cancellation = options.Cancellation ?? registration?.DefaultCancellation;

            Dictionary<string, object> normOptions = new Dictionary<string, object>();

            if (options.Headers != null)
            {
                normOptions["headers"] = options.Headers;
            }
            
            normOptions["max_attempts"] = options.MaxAttempts;
            
            if (options.RetryStrategy != null)
            {
                normOptions["retry_strategy"] = options.RetryStrategy;
            }
            
            if (cancellation != null)
            {
                normOptions["cancellation"] = cancellation;
            }

            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

            return await _db.SpawnTask(
                conn,
                options.Queue,
                taskName,
                JsonSerializer.Serialize(parameters),
                JsonSerializer.Serialize(normOptions)
            ).ConfigureAwait(false);
        }

        public async Task EmitEvent(EmitEventOptions options, string eventName, object? payload)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                throw new Exception("eventName required");
            }

            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
            
            await _db.EmitEvent(conn, options.Queue, eventName, JsonSerializer.Serialize(payload)).ConfigureAwait(false);
        }

        public async Task CancelTask(CancelTaskOptions options, string taskId)
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

            await _db.CancelTask(conn, options.Queue, taskId).ConfigureAwait(false);
        }

        public async Task<IEnumerable<ClaimedTask>> ClaimTasks(string queue, string workerId = "worker", int claimTimeout = 120, int batchSize = 1)
        {
            if (string.IsNullOrEmpty(queue))
            {
                throw new ArgumentException("Queue must be specified for claiming tasks");
            }

            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

            return await _db.ClaimTasks(conn, queue, workerId, claimTimeout, batchSize).ConfigureAwait(false);
        }

        public async Task WorkBatch(string queue, string workerId = "worker", int claimTimeout = 120, int batchSize = 1)
        {
            IEnumerable<ClaimedTask> tasks = await ClaimTasks(queue, workerId, claimTimeout, batchSize).ConfigureAwait(false);

            foreach (ClaimedTask task in tasks)
            {
                await ExecuteTask(task, queue, claimTimeout).ConfigureAwait(false);
            }
        }

        public async Task ExecuteTask(ClaimedTask task, string queue, int claimTimeout, bool fatalOnLeaseTimeout = false)
        {
            using CancellationTokenSource cts = new CancellationTokenSource();

            _ = Task
                .Delay(claimTimeout * 1000, cts.Token)
                .ContinueWith(t => 
                {
                    if (!t.IsCanceled)
                    {
                        _logger.LogWarning($"Task {task.TaskName} ({task.TaskId}) exceeded claim timeout of {claimTimeout}s");
                    }
            }, TaskScheduler.Default);

            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

            try
            {
                RegisteredTask? registration = _registry.ContainsKey(task.TaskName) ? _registry[task.TaskName] : null;

                TaskContext ctx = await TaskContext.CreateAsync(_logger, task.TaskId, conn, queue, task, claimTimeout).ConfigureAwait(false);

                if (registration == null)
                {
                    throw new Exception($"Unknown task: {task.TaskName}");
                }

                Task<object> handlerTask = registration.Handler(ctx, task.Params);

                Task fatalTask = Task.Delay(Timeout.Infinite, cts.Token);

                if (fatalOnLeaseTimeout)
                {
                    fatalTask = Task
                        .Delay(claimTimeout * 1000 * 2, cts.Token)
                        .ContinueWith(t =>
                        {
                            if (!t.IsCanceled)
                            {
                                throw new FatalLeaseTimeoutException($"Task {task.TaskName} ({task.TaskId}) exceeded claim timeout by 2x.");
                            }
                        }, TaskScheduler.Default);
                }

                Task finishedTask = await Task.WhenAny(handlerTask, fatalTask).ConfigureAwait(false);

                if (finishedTask == fatalTask)
                {
                    await fatalTask.ConfigureAwait(false);
                }

                object result = await handlerTask.ConfigureAwait(false);

                await _db.CompleteRun(conn, queue, task.RunId, JsonSerializer.Serialize(result)).ConfigureAwait(false);
            }
            catch (Exception err)
            {
                if (err is SuspendTaskException || err is CancelledTaskException) return;

                _logger.LogError($"[absurd] task execution failed: {err.Message}");

                try
                {
                    var errorObj = new { name = err.GetType().Name, message = err.Message, stack = err.StackTrace };

                    await _db.FailRun(conn, queue, task.RunId, JsonSerializer.Serialize(errorObj)).ConfigureAwait(false);
                }
                catch (Exception failErr)
                {
                    _logger.LogError($"Failed to mark run as failed: {failErr.Message}");
                }

                if (err is FatalLeaseTimeoutException) throw;
            }
            finally
            {
                cts.Cancel();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
