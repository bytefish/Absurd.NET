namespace AbsurdSdk
{
    public interface IAbsurd
    {
        void RegisterTask(TaskRegistrationOptions options, TaskHandler handler);

        Task CreateQueue(string queueName);

        Task DropQueue(string queueName);

        Task<IEnumerable<string>> ListQueues();

        Task<SpawnResult> Spawn(SpawnOptions options, string taskName, object parameters);

        Task EmitEvent(EmitEventOptions options, string eventName, object? payload = null);

        Task CancelTask(CancelTaskOptions options, string taskId);

        Task<IEnumerable<ClaimedTask>> ClaimTasks(string queue, string workerId, int claimTimeout = 120, int batchSize = 1);

        Task WorkBatch(string queue, string workerId, int claimTimeout = 120, int batchSize = 1);

        Task ExecuteTask(ClaimedTask task, string queue, int claimTimeout, bool fatalOnLeaseTimeout = false);
    }
}
