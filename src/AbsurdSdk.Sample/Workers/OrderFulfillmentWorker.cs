// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using AbsurdSdk.Sample.Jobs;
using AbsurdSdk.Sample.Models;

namespace AbsurdSdk.Sample.Workers;

public class SampleOrderWorker : BackgroundService
{
    private readonly IAbsurd _client;
    private readonly IServiceProvider _provider;

    public SampleOrderWorker(IAbsurd client, IServiceProvider provider)
    {
        _client = client;
        _provider = provider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Register Jobs
        _client.UseJob<FulfillOrderJob, OrderData, FulfillOrderResult>(_provider, options =>
        {
            // Override the default queue name "orders-queue" for this worker instance
            options.Queue = "high-priority-orders";
            // We can also override other settings if needed, like max attempts
            options.DefaultMaxAttempts = 10;
        });

        // Setup the worker to poll the specific queue used in the override
        AbsurdWorker _absurdWorker = new AbsurdWorker(new WorkerOptions
        {
            Queue = "high-priority-orders",
            WorkerId = "web-worker-01",
            Concurrency = 4,
            PollInterval = 0.5,
            OnError = ex => Console.WriteLine($"[WORKER ERROR] {ex.Message}")
        }, _client);

        // Start the worker loop
        await _absurdWorker.ExecuteAsync(stoppingToken);
    }
}