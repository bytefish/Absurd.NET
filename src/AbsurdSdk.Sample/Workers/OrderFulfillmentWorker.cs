
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using AbsurdSdk.Sample.Models;
using AbsurdSdk.Sample.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AbsurdSdk.Sample.Workers
{
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
}
