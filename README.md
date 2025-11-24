# Absurd.NET #

This is a .NET implementation of the Absurd SDK, which has been described at:

* https://github.com/earendil-works/absurd

This is by no means a production-ready SDK. Think of it as a first attempt at a .NET API. Once everything is 
somewhat stable the API surface is going to change a lot.

## Usage

We start by defining a `IJob`, which is going to model an Order Fulfillment Task:

```csharp
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using AbsurdSdk.Sample.Models;
using AbsurdSdk.Sample.Services;
using System.Text.Json.Nodes;

namespace AbsurdSdk.Sample.Jobs;

public class FulfillOrderJob : IJob<OrderData, FulfillOrderResult>
{
    private readonly PaymentService _paymentService;
    private readonly ShippingService _shippingService;
    private readonly ILogger<FulfillOrderJob> _logger;

    // Define the registration options here
    public static TaskRegistrationOptions Options => new()
    {
        Name = "fulfill-order",
        Queue = "orders-queue",
        DefaultMaxAttempts = 3
    };

    // Constructor Injection works perfectly here!
    public FulfillOrderJob(
        PaymentService paymentService,
        ShippingService shippingService,
        ILogger<FulfillOrderJob> logger)
    {
        _paymentService = paymentService;
        _shippingService = shippingService;
        _logger = logger;
    }

    public async Task<FulfillOrderResult> ExecuteAsync(TaskContext ctx, OrderData order)
    {
        _logger.LogInformation("Processing Order {OrderId}", order.OrderId);

        // Process the Payment
        PaymentResult payment = await ctx.Step("charge-payment", async () =>
        {
            return await _paymentService.ChargeAsync(order.OrderId, order.Amount);
        });

        if (!payment.Success)
        {
            throw new Exception($"Payment failed: {payment.ErrorMessage}");
        }

        // Wait for Warehouse
        _logger.LogInformation("Waiting for pick signal...");

        JsonNode pickPayload = await ctx.AwaitEvent(
            eventName: $"order-picked:{order.OrderId}",
            stepName: "wait-for-picking"
        );

        // Ship
        ShippingResult shipment = await ctx.Step("ship-items", async () =>
        {
            return await _shippingService.ShipAsync(order.OrderId, order.Items);
        });

        return new FulfillOrderResult { Status = "Fulfilled", Tracking = shipment.TrackingNumber };
    }
}
```

We then register it in the `Program.cs` like this:

```csharp

// Register Services
builder.Services.AddSingleton<PaymentService>();
builder.Services.AddSingleton<ShippingService>();

// Build the Absurd Client
builder.Services.AddSingleton<IAbsurd>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Absurd>>();

    NpgsqlDataSource dataSource = NpgsqlDataSource.Create(DockerContainers.PostgresContainer.GetConnectionString());

    return new Absurd(logger, dataSource);
});

// Register Jobs
builder.Services.RegisterJob<FulfillOrderJob, OrderData, FulfillOrderResult>();
```

In the `Program.cs` we are defining the `Absurd` client, start the `BackgroundService` for the `AbsurdWorker`:

```csharp
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using AbsurdSdk;
using AbsurdSdk.Sample.Docker;
using AbsurdSdk.Sample.Models;
using AbsurdSdk.Sample.Services;
using AbsurdSdk.Sample.Workers;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Start Docker Containers for dependencies
await DockerContainers.StartAllContainersAsync();

// Add Logging
builder.Services.AddLogging();

// Register Services
builder.Services.AddSingleton<PaymentService>();
builder.Services.AddSingleton<ShippingService>();

// Build the Absurd Client
builder.Services.AddSingleton<IAbsurd>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Absurd>>();

    NpgsqlDataSource dataSource = NpgsqlDataSource.Create(DockerContainers.PostgresContainer.GetConnectionString());

    return new Absurd(logger, dataSource);
});

// Register the Worker as a Hosted Service
builder.Services.AddHostedService<OrderFulfillmentWorker>();

var app = builder.Build();

// First create the queue if it doesn't exist
using (var scope = app.Services.CreateScope())
{
    var absurd = scope.ServiceProvider.GetRequiredService<IAbsurd>();

    await absurd.CreateQueue("orders-queue");
}

app.MapPost("/order", async (IAbsurd client, [FromBody] OrderData request) =>
{
    // Start the workflow with explicit options
    var result = await client.Spawn(new SpawnOptions
    {
        Queue = "orders-queue",
        MaxAttempts = 3
    }, "fulfill-order", request);

    return Results.Ok(new { Message = "Order started", RunId = result.RunId });
});

app.MapPost("/order/{orderId}/picked", async (IAbsurd client, string orderId, [FromBody] PickingData data) =>
{
    // This wakes up the suspended task waiting for "order-picked:{orderId}"
    await client.EmitEvent(
        eventName: $"order-picked:{orderId}",
        payload: data,
        options: new EmitEventOptions { Queue = "orders-queue" }
    );

    return Results.Ok(new { Message = "Pick signal sent. Workflow will resume." });
});

app.Run();
```

We can then create a Background Worker to poll for Tasks to run:

```csharp
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
        AbsurdWorker _absurdWorker = new AbsurdWorker(new WorkerOptions
        {
            Queue = "orders-queue",
            WorkerId = "web-worker-01",
            Concurrency = 4,
            PollInterval = 0.5,
            OnError = ex => Console.WriteLine($"[WORKER ERROR] {ex.Message}")
        }, _client);

        // Start the worker loop
        await _absurdWorker.ExecuteAsync(stoppingToken);
    }
}
```

And finally we define two endpoints to create an order and emit events to it:

```csharp
app.MapPost("/order", async (IAbsurd client, [FromBody] OrderData request) =>
{
    // Start the workflow with explicit options
    var result = await client.Spawn(new SpawnOptions
    {
        Queue = "orders-queue",
        MaxAttempts = 3
    }, "fulfill-order", request);

    return Results.Ok(new { Message = "Order started", RunId = result.RunId });
});

app.MapPost("/order/{orderId}/picked", async (IAbsurd client, string orderId, [FromBody] PickingData data) =>
{
    // This wakes up the suspended task waiting for "order-picked:{orderId}"
    await client.EmitEvent(
        eventName: $"order-picked:{orderId}",
        payload: data,
        options: new EmitEventOptions { Queue = "orders-queue" }
    );

    return Results.Ok(new { Message = "Pick signal sent. Workflow will resume." });
});

app.Run();
```

To kick off an Order send the JSON Payload to the endpoints:

```http
### Creates Order "ORD-123"
POST https://localhost:5000/order
Content-Type: application/json
Accept-Language: en-US,en;q=0.5
{
  "orderId": "ORD-123",
  "amount": 99.50,
  "items": ["Item A", "Item B"]
}

### Continues running Order "ORD-123"
POST https://localhost:5000/order/ORD-123/picked
{ 
  "picker": "Philipp" 
}
```

