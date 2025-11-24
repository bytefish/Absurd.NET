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
        _client.UseJob<FulfillOrderJob, OrderData, FulfillOrderResult>(_provider);

        // Setup the worker to poll the queue
        AbsurdWorker absurdWorker = new AbsurdWorker(new WorkerOptions
        {
            Queue = "orders-queue",
            WorkerId = "web-worker-01",
            Concurrency = 4,
            PollInterval = 0.5,
            OnError = ex => Console.WriteLine($"[WORKER ERROR] {ex.Message}")
        }, _client);

        // Start the worker loop
        await absurdWorker.ExecuteAsync(stoppingToken);
    }
}