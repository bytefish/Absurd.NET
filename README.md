# Absurd.NET #

This is a .NET implementation of the Absurd SDK, which has been described at:

* https://github.com/earendil-works/absurd

This is by no means a production-ready SDK. Think of it as a first attempt at a .NET API. Once everything is 
somewhat stable the API surface is going to change a lot.

## Usage

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

The Task Implementation and the Worker Loop are put in a `BackgroundService`:

```csharp
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using AbsurdSdk.Sample.Models;
using AbsurdSdk.Sample.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AbsurdSdk.Sample.Workers;

public class OrderFulfillmentWorker : BackgroundService
{
    private readonly IAbsurd _client;
    private readonly ILogger<OrderFulfillmentWorker> _logger;
    private readonly PaymentService _paymentService;
    private readonly ShippingService _shippingService;

    public OrderFulfillmentWorker(
        IAbsurd client,
        ILogger<OrderFulfillmentWorker> logger,
        PaymentService paymentService,
        ShippingService shippingService)
    {
        _client = client;
        _logger = logger;
        _paymentService = paymentService;
        _shippingService = shippingService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Register Tasks
        _client.RegisterTask(new TaskRegistrationOptions
        {
            Name = "fulfill-order",
            Queue = "orders-queue",
            DefaultMaxAttempts = 3
        }, HandleOrderFulfillment);

        _logger.LogInformation("Starting Absurd Worker...");

        // Create the SDK Worker
        var worker = new AbsurdWorker(new WorkerOptions
        {
            Queue = "orders-queue",
            Concurrency = 5,
            WorkerId = $"node-{Environment.ProcessId}",
            OnError = (ex) => _logger.LogError(ex, "Worker Loop Error")
        }, _client);

        // Run until cancelled
        await worker.ExecuteAsync(stoppingToken);
    }

    private async Task<object> HandleOrderFulfillment(TaskContext ctx, JsonNode? paramsNode)
    {
        var order = paramsNode.Deserialize<OrderData>();

        if (order == null)
        {
            throw new ArgumentNullException(nameof(order));
        }

        _logger.LogInformation("Processing Order {OrderId}", order.OrderId);

        // Process the Payment
        var payment = await ctx.Step("charge-payment", async () =>
        {
            return await _paymentService.ChargeAsync(order.OrderId, order.Amount);
        });

        if (!payment!.Success)
        {
            throw new Exception($"Payment failed: {payment.ErrorMessage}");
        }

        // Wait for Warehouse Event
        _logger.LogInformation("Payment successful. Waiting for warehouse pick signal...");

        var pickPayload = await ctx.AwaitEvent(
            eventName: $"order-picked:{order.OrderId}",
            stepName: "wait-for-picking"
        );

        // We can read data sent by the event emitter
        string pickerName = pickPayload?["picker"]?.ToString() ?? "Unknown";

        _logger.LogInformation("Item picked by {Picker}. Resuming workflow...", pickerName);

        // Ship Items
        ShippingResult? shipment = await ctx.Step("ship-items", async () =>
        {
            return await _shippingService.ShipAsync(order.OrderId, order.Items);
        });

        _logger.LogInformation("Order {OrderId} Complete.", order.OrderId);

        return new { Status = "Fulfilled", Tracking = shipment!.TrackingNumber };
    }
}
```

We can then create a new Order with a given Order ID and continue running it by using:

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

