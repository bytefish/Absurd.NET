// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AbsurdSdk;

// Delegate for Task Handler
public delegate Task<object> TaskHandler(TaskContext ctx, JsonNode? parameters);

internal class RegisteredTask
{
    public required string Name { get; set; }
    public required string Queue { get; set; }
    public int DefaultMaxAttempts { get; set; } = 5;
    public CancellationPolicy? DefaultCancellation { get; set; }
    public required TaskHandler Handler { get; set; }
}

public class TaskContext
{
    private readonly AbsurdDatabase _db = new AbsurdDatabase();
    private readonly Dictionary<string, int> _stepNameCounter = new();
    private readonly ILogger _logger;

    public string TaskId { get; }

    private readonly NpgsqlConnection _connection;
    private readonly string _queueName;
    private readonly ClaimedTask _task;
    private readonly Dictionary<string, JsonNode?> _checkpointCache;
    private readonly int _claimTimeout;

    private TaskContext(
        ILogger logger,
        string taskId,
        NpgsqlConnection con,
        string queueName,
        ClaimedTask task,
        Dictionary<string, JsonNode?> checkpointCache,
        int claimTimeout)
    {
        _logger = logger;
        TaskId = taskId;
        _connection = con;
        _queueName = queueName;
        _task = task;
        _checkpointCache = checkpointCache;
        _claimTimeout = claimTimeout;
    }

    public static async Task<TaskContext> CreateAsync(
        ILogger logger,
        string taskId,
        NpgsqlConnection con,
        string queueName,
        ClaimedTask task,
        int claimTimeout)
    {
        AbsurdDatabase db = new AbsurdDatabase();

        IEnumerable<CheckpointRow> checkpoints = await db.GetCheckpointStates(con, queueName, task.TaskId, task.RunId).ConfigureAwait(false);

        Dictionary<string, JsonNode?> cache = new Dictionary<string, JsonNode?>();

        foreach (CheckpointRow cp in checkpoints)
        {
            cache[cp.CheckpointName] = cp.State;
        }

        return new TaskContext(logger, taskId, con, queueName, task, cache, claimTimeout);
    }

    public async Task<T?> Step<T>(string name, Func<Task<T>> fn)
    {
        string checkpointName = GetCheckpointName(name);
        JsonNode? state = await LookupCheckpoint(checkpointName).ConfigureAwait(false);

        if (state != null)
        {
            return state.Deserialize<T>();
        }

        T? rv = await fn().ConfigureAwait(false);

        // Serialize result to string for DB
        JsonNode? rvJson = JsonSerializer.SerializeToNode(rv);

        string rvString = rvJson?.ToJsonString() ?? "null";

        await _db.PersistCheckpoint(_connection, _queueName, _task.TaskId, _task.RunId, checkpointName, rvString, _claimTimeout).ConfigureAwait(false);

        _checkpointCache[checkpointName] = rvJson;
        return rv;
    }

    public async Task SleepFor(string stepName, double durationSeconds)
    {
        await SleepUntil(stepName, DateTime.UtcNow.AddSeconds(durationSeconds)).ConfigureAwait(false);
    }

    public async Task SleepUntil(string stepName, DateTime wakeAt)
    {
        string checkpointName = GetCheckpointName(stepName);
        JsonNode? state = await LookupCheckpoint(checkpointName).ConfigureAwait(false);

        DateTime actualWakeAt = wakeAt;

        if (state != null && state.GetValueKind() == JsonValueKind.String)
        {
            actualWakeAt = state.GetValue<DateTime>();
        }
        else if (state == null)
        {
            // Persist the wake time as the state
            string wakeString = JsonSerializer.Serialize(wakeAt);
            await _db.PersistCheckpoint(_connection, _queueName, _task.TaskId, _task.RunId, checkpointName, wakeString, _claimTimeout).ConfigureAwait(false);
            _checkpointCache[checkpointName] = JsonValue.Create(wakeAt);
        }

        if (DateTime.UtcNow < actualWakeAt)
        {
            await _db.ScheduleRun(_connection, _queueName, _task.RunId, actualWakeAt).ConfigureAwait(false);

            throw new SuspendTaskException();
        }
    }

    private string GetCheckpointName(string name)
    {
        if (!_stepNameCounter.ContainsKey(name))
        {
            _stepNameCounter[name] = 0;
        }

        _stepNameCounter[name]++;
        int count = _stepNameCounter[name];
        return count == 1 ? name : $"{name}#{count}";
    }

    private async Task<JsonNode?> LookupCheckpoint(string checkpointName)
    {
        if (_checkpointCache.TryGetValue(checkpointName, out JsonNode? cached))
        {
            return cached;
        }

        JsonNode? state = await _db.GetSingleCheckpoint(_connection, _queueName, _task.TaskId, checkpointName).ConfigureAwait(false);

        if (state != null)
        {
            _checkpointCache[checkpointName] = state;

            return state;
        }

        return null;
    }

    public async Task<JsonNode> AwaitEvent(string eventName, string? stepName = null, double? timeoutSeconds = null)
    {
        stepName ??= $"$awaitEvent:{eventName}";
        int? timeout = timeoutSeconds.HasValue ? (int)Math.Floor(timeoutSeconds.Value) : null;

        string checkpointName = GetCheckpointName(stepName);

        JsonNode? cached = await LookupCheckpoint(checkpointName).ConfigureAwait(false);

        if (cached != null) return cached;

        if (_task.WakeEvent == eventName && _task.EventPayload == null)
        {
            _task.WakeEvent = null;
            _task.EventPayload = null;
            throw new TimeoutErrorException($"Timed out waiting for event \"{eventName}\"");
        }

        (bool ShouldSuspend, JsonNode Payload) result = await _db.AwaitEvent(_connection, _queueName, _task.TaskId, _task.RunId, checkpointName, eventName, timeout).ConfigureAwait(false);

        if (!result.ShouldSuspend)
        {
            _checkpointCache[checkpointName] = result.Payload;
            _task.EventPayload = null;
            return result.Payload;
        }

        throw new SuspendTaskException();
    }

    public async Task Heartbeat(int? seconds = null)
    {
        await _db.Heartbeat(_connection, _queueName, _task.RunId, seconds ?? _claimTimeout).ConfigureAwait(false);
    }

    public async Task EmitEvent(string eventName, JsonNode? payload = null)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            throw new ArgumentException("eventName must be a non-empty string");
        }

        await _db.EmitEvent(_connection, _queueName, eventName, payload?.ToJsonString() ?? "null").ConfigureAwait(false);
    }
}