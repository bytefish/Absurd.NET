// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using AbsurdSdk.Core;
using AbsurdSdk.Database;
using AbsurdSdk.Exceptions;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;

namespace AbsurdSdk;

/// <summary>
/// The Absurd client is the main entry point for interacting with the Absurd task queue system. It provides methods 
/// for registering tasks, spawning new tasks, emitting events, claiming and executing tasks, and managing queues. 
/// 
/// The client maintains a registry of task handlers and uses a PostgreSQL database to store task and event data. It 
/// also handles task execution logic, including retry strategies, cancellation policies, and error handling.
/// </summary>
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

    /// <summary>
    /// Registers a task handler with the Absurd client. This allows the client to execute tasks of the specified type when they are claimed.
    /// </summary>
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

    /// <summary>
    /// Creates a new queue with the specified name. Queues are used to organize tasks and determine which workers can claim 
    /// and execute them. This method must be called before spawning tasks to a new queue or claiming tasks from it.
    /// </summary>
    public async Task CreateQueueAsync(string queueName)
    {
        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

        await _db.CreateQueueAsync(conn, queueName).ConfigureAwait(false);
    }

    /// <summary>
    /// Drops the specified queue and all associated tasks and events. This is a destructive operation that cannot be 
    /// undone, so use with caution.
    /// </summary>
    public async Task DropQueueAsync(string queueName)
    {
        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

        await _db.DropQueueAsync(conn, queueName).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a list of all existing queues in the Absurd system. This can be used to discover available queues 
    /// for spawning tasks or claiming work.
    /// </summary>
    public async Task<IEnumerable<string>> ListQueuesAsync()
    {
        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
        return await _db.ListQueuesAsync(conn).ConfigureAwait(false);
    }

    /// <summary>
    /// Spawns a new task in the specified queue with the given parameters and options. The task will be picked up by workers 
    /// that have registered handlers for the specified task name. The options allow you to configure retry strategies, 
    /// cancellation policies, and other task execution parameters. 
    /// 
    /// The method returns a SpawnResult containing the task ID and run ID for tracking the task's progress.
    /// </summary>
    public async Task<SpawnResult> SpawnAsync(SpawnOptions options, string taskName, object parameters)
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

        return await _db.SpawnTaskAsync(
            conn,
            options.Queue,
            taskName,
            JsonSerializer.Serialize(parameters),
            JsonSerializer.Serialize(normOptions)
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// Emits an event with the specified name and payload to the given queue. Events are a way to trigger actions in 
    /// response to certain conditions, such as task completions, failures, or custom application events. Workers 
    /// can listen for specific events and execute handlers when those events are emitted. 
    /// </summary>
    public async Task EmitEventAsync(EmitEventOptions options, string eventName, object? payload)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            throw new Exception("eventName required");
        }

        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
        
        await _db.EmitEventAsync(conn, options.Queue, eventName, JsonSerializer.Serialize(payload)).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels a task with the specified ID in the given queue. This will prevent the task from being executed if it has not 
    /// already been claimed by a worker. If the task is currently being executed, the cancellation policy will determine 
    /// how the worker should respond (e.g. whether to allow the task to finish, attempt to stop it, or mark it as cancelled).
    /// </summary>
    public async Task CancelTaskAsync(CancelTaskOptions options, string taskId)
    {
        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

        await _db.CancelTaskAsync(conn, options.Queue, taskId).ConfigureAwait(false);
    }

    /// <summary>
    /// Claims a Task from the specified queue for execution. This method is typically called by worker processes that are 
    /// polling for work.
    /// </summary>
    public async Task<IEnumerable<ClaimedTask>> ClaimTasksAsync(string queue, string workerId = "worker", int claimTimeout = 120, int batchSize = 1)
    {
        if (string.IsNullOrEmpty(queue))
        {
            throw new ArgumentException("Queue must be specified for claiming tasks");
        }

        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

        return await _db.ClaimTasksAsync(conn, queue, workerId, claimTimeout, batchSize).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes a batch of claimed tasks from the specified queue. This method is typically called by worker processes that are 
    /// polling for work. It claims a batch of tasks and then executes each one using the registered handlers.
    /// </summary>
    public async Task WorkBatchAsync(string queue, string workerId = "worker", int claimTimeout = 120, int batchSize = 1)
    {
        IEnumerable<ClaimedTask> tasks = await ClaimTasksAsync(queue, workerId, claimTimeout, batchSize).ConfigureAwait(false);

        foreach (ClaimedTask task in tasks)
        {
            await ExecuteTaskAsync(task, queue, claimTimeout).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes a claimed task using the registered handler for its task type. This method handles the entire lifecycle of 
    /// a task execution, including invoking the handler, managing timeouts, handling exceptions, and marking the task 
    /// as completed or failed in the database.
    /// </summary>
    public async Task ExecuteTaskAsync(ClaimedTask task, string queue, int claimTimeout, bool fatalOnLeaseTimeout = false)
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

            await _db.CompleteRunAsync(conn, queue, task.RunId, JsonSerializer.Serialize(result)).ConfigureAwait(false);
        }
        catch (Exception err)
        {
            if (err is SuspendTaskException || err is CancelledTaskException) return;

            _logger.LogError($"[absurd] task execution failed: {err.Message}");

            try
            {
                var errorObj = new { name = err.GetType().Name, message = err.Message, stack = err.StackTrace };

                await _db.FailRunAsync(conn, queue, task.RunId, JsonSerializer.Serialize(errorObj)).ConfigureAwait(false);
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
