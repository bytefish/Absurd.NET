// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AbsurdSdk;

public class TaskRegistrationOptions
{
    public required string Name { get; set; }

    public required string Queue { get; set; }
    public int DefaultMaxAttempts { get; set; } = 5;

    public CancellationPolicy? DefaultCancellation { get; set; }
}

public class SpawnOptions
{
    public required string Queue { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public RetryStrategy? RetryStrategy { get; set; }

    public JsonObject? Headers { get; set; }

    public CancellationPolicy? Cancellation { get; set; }
}

public class EmitEventOptions
{
    public required string Queue { get; set; }
}

public class CancelTaskOptions
{
    public required string Queue { get; set; }
}

public enum RetryStrategyKind { Fixed, Exponential, None }

public class RetryStrategy
{
    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RetryStrategyKind Kind { get; set; }

    [JsonPropertyName("base_seconds")]
    public double? BaseSeconds { get; set; }

    [JsonPropertyName("factor")]
    public double? Factor { get; set; }

    [JsonPropertyName("max_seconds")]
    public double? MaxSeconds { get; set; }
}

public class CancellationPolicy
{
    [JsonPropertyName("max_duration")]
    public double? MaxDuration { get; set; }

    [JsonPropertyName("max_delay")]
    public double? MaxDelay { get; set; }
}

public class ClaimedTask
{
    public required string RunId { get; set; }

    public required string TaskId { get; set; }

    public required string TaskName { get; set; }

    public int Attempt { get; set; }

    public JsonNode? Params { get; set; }

    public JsonNode? RetryStrategy { get; set; }

    public int? MaxAttempts { get; set; }

    public JsonObject? Headers { get; set; }

    public string? WakeEvent { get; set; }

    public JsonNode? EventPayload { get; set; }
}

public class WorkerOptions
{
    public required string WorkerId { get; set; }

    public required string Queue { get; set; }

    public int ClaimTimeout { get; set; } = 120;

    public int? BatchSize { get; set; }

    public int Concurrency { get; set; } = 1;

    public double PollInterval { get; set; } = 0.25;

    public Action<Exception>? OnError { get; set; }

    public bool FatalOnLeaseTimeout { get; set; } = true;
}

public interface IWorker
{
    Task CloseAsync();
}

public class CheckpointRow
{
    public required string CheckpointName { get; set; }

    public JsonNode? State { get; set; }

    public required string Status { get; set; }

    public string? OwnerRunId { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public class SpawnResult
{
    public required string TaskId { get; set; }

    public required string RunId { get; set; }

    public int Attempt { get; set; }
}

public class SuspendTaskException : Exception
{
    public SuspendTaskException() : base("Task suspended") { }
}

public class CancelledTaskException : Exception
{
    public CancelledTaskException() : base("Task cancelled") { }
}

public class TimeoutErrorException : Exception
{
    public TimeoutErrorException(string message) : base(message) { }
}

public class FatalLeaseTimeoutException : Exception
{
    public FatalLeaseTimeoutException(string message) : base(message) { }
}