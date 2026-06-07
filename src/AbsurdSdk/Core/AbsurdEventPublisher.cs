// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace AbsurdSdk.Core;

/// <summary>
/// Provides an implementation of the IEventPublisher interface that publishes events to an Absurd event queue.
/// </summary>
/// <remarks>This class is intended for internal use to integrate with the Absurd event system. It delegates event
/// publishing to an underlying IAbsurd client. Thread safety and error handling depend on the behavior of the provided
/// IAbsurd implementation.</remarks>
internal class AbsurdEventPublisher : IEventPublisher
{
    private readonly IAbsurd _client;

    /// <summary>
    /// Initializes a new instance of the AbsurdEventPublisher class using the specified Absurd client.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the Absurd service. Cannot be null.</param>
    public AbsurdEventPublisher(IAbsurd client)
    {
        _client = client;
    }

    /// <summary>
    /// Asynchronously emits an event with the specified payload to the given queue.
    /// </summary>
    /// <typeparam name="TPayload">The type of the payload to include with the event.</typeparam>
    /// <param name="queue">The name of the queue to which the event will be emitted. Cannot be null or empty.</param>
    /// <param name="eventName">The name of the event to emit. Cannot be null or empty.</param>
    /// <param name="payload">The payload data to include with the event.</param>
    /// <returns>A task that represents the asynchronous emit operation.</returns>
    public async Task EmitEventAsync<TPayload>(string queue, string eventName, TPayload payload)
    {
        await _client.SpawnAsync(new SpawnOptions { Queue = queue }, eventName, payload);
    }
}