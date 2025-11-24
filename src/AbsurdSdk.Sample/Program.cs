// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using AbsurdSdk;
using AbsurdSdk.Sample.Docker;
using AbsurdSdk.Sample.Jobs;
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

// Register Jobs
builder.Services.RegisterJob<FulfillOrderJob, OrderData, FulfillOrderResult>();

// Register the Worker as a Hosted Service
builder.Services.AddHostedService<SampleOrderWorker>();

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