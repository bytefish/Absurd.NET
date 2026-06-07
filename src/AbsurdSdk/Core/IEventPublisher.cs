namespace AbsurdSdk.Core;

public interface IEventPublisher
{
    Task EmitEventAsync<TPayload>(string queue, string eventName, TPayload payload);
}
