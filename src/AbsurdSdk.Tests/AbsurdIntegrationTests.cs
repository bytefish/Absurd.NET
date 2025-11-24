// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using AbsurdSdk.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AbsurdSdk.Sample;

[TestClass]
public class AbsurdIntegrationTests
{
    private static string ConnectionString = null!;

    /// <summary>
    /// Starts the Containers for the Tests.
    /// </summary>
    /// <param name="context">Required Test Context</param>
    /// <returns>Awaitable Task</returns>
    [AssemblyInitialize]
    public static async Task AssemblyInitializeAsync(TestContext context)
    {
        await DockerContainers.StartAllContainersAsync();

        ConnectionString = DockerContainers.PostgresContainer.GetConnectionString();
    }


    [TestMethod]
    public async Task Test_BasicTaskExecution_Flow()
    {
        // ARRANGE
        NpgsqlDataSource dataSource = NpgsqlDataSource.Create(ConnectionString);

        // Build the Absurd Client
        IAbsurd client = new Absurd(NullLogger<Absurd>.Instance, dataSource);

        // Ensure the test queue exists
        await client.CreateQueue("test-queue");

        // We use a TCS to signal when the background worker has actually finished the task
        var completionSource = new TaskCompletionSource<int>();

        // Define the Task Logic
        client.RegisterTask(new TaskRegistrationOptions
        {
            Queue = "test-queue",
            Name = "add-numbers"
        }, async (ctx, parameters) =>
        {
            if(parameters ==  null)
            {
                throw new InvalidOperationException("Expected JsonObject parameters");
            }
            // Extract inputs
            int a = parameters["a"]?.GetValue<int>() ?? 0;
            int b = parameters["b"]?.GetValue<int>() ?? 0;

            var sum = a + b;

            // Signal the test that we are done
            completionSource.SetResult(sum);

            return new { result = sum };
        });

        // ACT
        await client.Spawn(new SpawnOptions { Queue = "test-queue" }, "add-numbers", new { a = 10, b = 20 });

        AbsurdWorker worker = new AbsurdWorker(new WorkerOptions
        {
            Queue = "test-queue",
            PollInterval = 0.1, // Fast polling for tests
            Concurrency = 1,
            WorkerId = "test-worker"
        }, client);

        using CancellationTokenSource cts = new CancellationTokenSource();

        // Run worker in background
        Task workerTask = worker.ExecuteAsync(cts.Token);

        // Wait for the task to complete (or timeout after 5s)
        Task completedTask = await Task.WhenAny(completionSource.Task, Task.Delay(5000));

        // Stop worker
        cts.Cancel();
        
        try 
        { 
            await workerTask; 
        } 
        catch (OperationCanceledException) { }

        if (completedTask != completionSource.Task)
        {
            Assert.Fail("Task execution timed out.");
        }

        int result = await completionSource.Task;

        Assert.AreEqual(30, result, "The worker should have summed 10 + 20 to get 30.");
    }
}