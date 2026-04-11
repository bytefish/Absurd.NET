// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using AbsurdSdk.Core;

namespace AbsurdSdk
{
    /// <summary>
    /// The main interface for interacting with the Absurd task management system. This interface defines the core operations for registering task handlers, 
    /// and managing message queues, spawning tasks, emitting events, claiming tasks for execution, and processing tasks. It serves as the primary entry 
    /// point for developers using the Absurd SDK.
    /// </summary>
    public interface IAbsurd
    {
        /// <summary>
        /// Registers a task handler with the Absurd system. The handler will be invoked when a task with 
        /// the corresponding name is spawned.
        /// </summary>
        void RegisterTask(TaskRegistrationOptions options, TaskHandler handler);

        /// <summary>
        /// Creates a new message queue with the specified name if it does not already exist.
        /// </summary>
        Task CreateQueueAsync(string queueName);

        /// <summary>
        /// Deletes the message queue with the specified name and all tasks associated with it. This is a destructive operation that cannot be undone, so use with caution. 
        /// 
        /// It will remove all tasks in the queue, including pending, claimed, and failed tasks. It will also remove the queue itself, so it will no longer be available 
        /// for spawning or claiming tasks.
        /// </summary>
        Task DropQueueAsync(string queueName);

        /// <summary>
        /// Returns a list of all existing message queues in the Absurd system. This can be used to discover available queues for spawning and claiming tasks.
        /// </summary>
        Task<IEnumerable<string>> ListQueuesAsync();

        /// <summary>
        /// Spawns a new task with the specified name and parameters, and enqueues it onto the specified message queue. The task will be picked up by 
        /// a registered handler for execution.
        /// </summary>
        Task<SpawnResult> SpawnAsync(SpawnOptions options, string taskName, object parameters);

        /// <summary>
        /// Emits a custom event with the specified name and payload to the specified message queue. This can be used for inter-task communication, 
        /// triggering workflows, or any other use case where you want to send a message to a queue without spawning a task. The event will be 
        /// delivered to any handlers that are listening for it on the queue.
        /// </summary>
        Task EmitEventAsync(EmitEventOptions options, string eventName, object? payload = null);

        /// <summary>
        /// Cancels a pending or claimed task with the specified ID. The task will be removed from the queue and will not be executed if it has not 
        /// already started.
        /// </summary>
        /// <param name="options"></param>
        /// <param name="taskId"></param>
        /// <returns></returns>
        Task CancelTaskAsync(CancelTaskOptions options, string taskId);

        /// <summary>
        /// Claims one or more tasks from the specified message queue for execution by the worker with the given ID. The claimed tasks 
        /// will be locked for the specified claim timeout duration, during which they will not be available for other workers to claim.
        /// </summary>
        Task<IEnumerable<ClaimedTask>> ClaimTasksAsync(string queue, string workerId, int claimTimeout = 120, int batchSize = 1);

        /// <summary>
        /// Processes a batch of tasks from the specified message queue. This method will claim a batch of tasks and execute them 
        /// sequentially using the registered handlers.
        /// </summary>
        Task WorkBatchAsync(string queue, string workerId, int claimTimeout = 120, int batchSize = 1);

        /// <summary>
        /// Executes a single claimed task using the registered handler for its task name. This method will handle the execution of the task and 
        /// return the result. It will also handle any exceptions that occur during execution and update the task status accordingly. 
        /// </summary>
        Task ExecuteTaskAsync(ClaimedTask task, string queue, int claimTimeout, bool fatalOnLeaseTimeout = false);
    }
}
