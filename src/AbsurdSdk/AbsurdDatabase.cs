// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Npgsql;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AbsurdSdk;

/// <summary>
/// Encapsulates all raw database interactions.
/// </summary>
public class AbsurdDatabase
{
    public async Task CreateQueue(NpgsqlConnection conn, string queueName)
    {
        using NpgsqlCommand cmd = new("SELECT absurd.create_queue(@queueName)", conn);

        AddParam(cmd, "queueName", queueName);

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task DropQueue(NpgsqlConnection conn, string queueName)
    {
        using NpgsqlCommand cmd = new("SELECT absurd.drop_queue(@queueName)", conn);

        AddParam(cmd, "queueName", queueName);

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<IEnumerable<string>> ListQueues(NpgsqlConnection conn)
    {
        List<string> results = new();

        using NpgsqlCommand cmd = new("SELECT queue_name FROM absurd.list_queues()", conn);
        
        using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            results.Add(reader.GetString(0));
        }
        return results;
    }

    public async Task<SpawnResult> SpawnTask(NpgsqlConnection conn, string queue, string taskName, string paramsJson, string optionsJson)
    {
        using NpgsqlCommand cmd = new("SELECT task_id, run_id, attempt FROM absurd.spawn_task(@queue, @taskName, @paramsJson::jsonb, @optionsJson::jsonb)", conn);

        AddParam(cmd, "queue", queue);
        AddParam(cmd, "taskName", taskName);
        AddParam(cmd, "paramsJson", paramsJson);
        AddParam(cmd, "optionsJson", optionsJson);

        using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            return new SpawnResult
            {
                TaskId = reader.GetGuid(0).ToString(),
                RunId = reader.GetGuid(1).ToString(),
                Attempt = reader.GetInt32(2)
            };
        }
        throw new Exception("Failed to spawn task");
    }

    public async Task CancelTask(NpgsqlConnection conn, string queue, string taskId)
    {
        using NpgsqlCommand cmd = new("SELECT absurd.cancel_task(@queue, @taskId)", conn);
        
        AddParam(cmd, "queue", queue);
        AddParam(cmd, "taskId", Guid.Parse(taskId));

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task EmitEvent(NpgsqlConnection conn, string queue, string eventName, string payloadJson)
    {
        using NpgsqlCommand cmd = new("SELECT absurd.emit_event(@queue, @eventName, @payloadJson::jsonb)", conn);

        AddParam(cmd, "queue", queue);
        AddParam(cmd, "eventName", eventName);
        AddParam(cmd, "payloadJson", payloadJson);

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<IEnumerable<ClaimedTask>> ClaimTasks(NpgsqlConnection conn, string queue, string workerId, int timeout, int count)
    {
        List<ClaimedTask> tasks = new();
        using NpgsqlCommand cmd = new(
            @"SELECT run_id, task_id, attempt, task_name, params, retry_strategy, 
                         max_attempts, headers, wake_event, event_payload
                  FROM absurd.claim_task(@queue, @workerId, @timeout, @count)", conn);

        AddParam(cmd, "queue", queue);
        AddParam(cmd, "workerId", workerId);
        AddParam(cmd, "timeout", timeout);
        AddParam(cmd, "count", count);

        using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            tasks.Add(new ClaimedTask
            {
                RunId = reader.GetGuid(0).ToString(),
                TaskId = reader.GetGuid(1).ToString(),
                Attempt = reader.GetInt32(2),
                TaskName = reader.GetString(3),
                Params = ParseJson(reader, 4),
                RetryStrategy = ParseJson(reader, 5),
                MaxAttempts = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                Headers = reader.IsDBNull(7) ? null : JsonSerializer.Deserialize<JsonObject>(reader.GetString(7)),
                WakeEvent = reader.IsDBNull(8) ? null : reader.GetString(8),
                EventPayload = ParseJson(reader, 9)
            });
        }
        return tasks;
    }

    public async Task CompleteRun(NpgsqlConnection conn, string queue, string runId, string resultJson)
    {
        using NpgsqlCommand cmd = new("SELECT absurd.complete_run(@queue, @runId, @resultJson::jsonb)", conn);

        AddParam(cmd, "queue", queue);
        AddParam(cmd, "runId", Guid.Parse(runId));
        AddParam(cmd, "resultJson", resultJson);

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task FailRun(NpgsqlConnection conn, string queue, string runId, string errorJson)
    {
        using NpgsqlCommand cmd = new("SELECT absurd.fail_run(@queue, @runId, @errorJson::jsonb, null)", conn);

        AddParam(cmd, "queue", queue);
        AddParam(cmd, "runId", Guid.Parse(runId));
        AddParam(cmd, "errorJson", errorJson);

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<IEnumerable<CheckpointRow>> GetCheckpointStates(NpgsqlConnection conn, string queue, string taskId, string runId)
    {
        List<CheckpointRow> rows = new();

        using NpgsqlCommand cmd = new("SELECT checkpoint_name, state, status, owner_run_id, updated_at FROM absurd.get_task_checkpoint_states(@queue, @taskId, @runId)", conn);

        AddParam(cmd, "queue", queue);
        AddParam(cmd, "taskId", Guid.Parse(taskId));
        AddParam(cmd, "runId", Guid.Parse(runId));

        using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            rows.Add(new CheckpointRow
            {
                CheckpointName = reader.GetString(0),
                State = ParseJson(reader, 1),
                Status = reader.GetString(2),
                OwnerRunId = reader.IsDBNull(3) ? null : reader.GetGuid(3).ToString(),
                UpdatedAt = reader.GetDateTime(4)
            });
        }
        return rows;
    }

    public async Task<JsonNode?> GetSingleCheckpoint(NpgsqlConnection conn, string queue, string taskId, string checkpointName)
    {
        using NpgsqlCommand cmd = new("SELECT state FROM absurd.get_task_checkpoint_state(@queue, @taskId, @checkpointName)", conn);

        AddParam(cmd, "queue", queue);
        AddParam(cmd, "taskId", Guid.Parse(taskId));
        AddParam(cmd, "checkpointName", checkpointName);

        using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            return ParseJson(reader, 0);
        }

        return null;
    }

    public async Task PersistCheckpoint(NpgsqlConnection conn, string queue, string taskId, string runId, string checkpointName, string stateJson, int timeout)
    {
        await ExecuteWithCancelCheck(async () =>
        {
            using NpgsqlCommand cmd = new(
                "SELECT absurd.set_task_checkpoint_state(@queue, @taskId, @checkpointName, @stateJson::jsonb, @runId, @timeout)", conn);

            AddParam(cmd, "queue", queue);
            AddParam(cmd, "taskId", Guid.Parse(taskId));
            AddParam(cmd, "checkpointName", checkpointName);
            AddParam(cmd, "stateJson", stateJson);
            AddParam(cmd, "runId", Guid.Parse(runId));
            AddParam(cmd, "timeout", timeout);

            return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        });
    }

    public async Task ScheduleRun(NpgsqlConnection conn, string queue, string runId, DateTime wakeAt)
    {
        using NpgsqlCommand cmd = new("SELECT absurd.schedule_run(@queue, @runId, @wakeAt)", conn);

        AddParam(cmd, "queue", queue);
        AddParam(cmd, "runId", Guid.Parse(runId));
        AddParam(cmd, "wakeAt", wakeAt);

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task Heartbeat(NpgsqlConnection conn, string queue, string runId, int seconds)
    {
        await ExecuteWithCancelCheck(async () =>
        {
            using NpgsqlCommand cmd = new("SELECT absurd.extend_claim(@queue, @runId, @seconds)", conn);

            AddParam(cmd, "queue", queue);
            AddParam(cmd, "runId", Guid.Parse(runId));
            AddParam(cmd, "seconds", seconds);

            return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        });
    }

    public async Task<(bool ShouldSuspend, JsonNode Payload)> AwaitEvent(NpgsqlConnection conn, string queue, string taskId, string runId, string checkpointName, string eventName, int? timeout)
    {
        return await ExecuteWithCancelCheck(async () =>
        {
            using NpgsqlCommand cmd = new("SELECT should_suspend, payload FROM absurd.await_event(@queue, @taskId, @runId, @checkpointName, @eventName, @timeout)", conn);
            
            AddParam(cmd, "queue", queue);
            AddParam(cmd, "taskId", Guid.Parse(taskId));
            AddParam(cmd, "runId", Guid.Parse(runId));
            AddParam(cmd, "checkpointName", checkpointName);
            AddParam(cmd, "eventName", eventName);
            AddParam(cmd, "timeout", timeout);

            using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            
            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                return (
                    reader.GetBoolean(0),
                    ParseJson(reader, 1)
                );
            }

            throw new Exception("Failed to await event");
        });
    }

    private static JsonNode? ParseJson(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return JsonSerializer.Deserialize<JsonNode>(reader.GetString(ordinal));
    }

    private async Task<T> ExecuteWithCancelCheck<T>(Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == "AB001")
        {
            throw new CancelledTaskException();
        }
    }

    private void AddParam(NpgsqlCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}