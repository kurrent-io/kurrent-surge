using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Processor.OrderEnrichment.Events;

namespace Processor.OrderEnrichment;

sealed class OrderSeeder(KurrentClient client, ILogger<OrderSeeder> logger) : BackgroundService {
    static readonly CustomerUpdated[] Customers = [
        new("cust-1", "Alice Smith", "alice@example.com", "Gold", DateTimeOffset.UtcNow),
        new("cust-2", "Bob Jones", "bob@example.com", "Silver", DateTimeOffset.UtcNow),
        new("cust-3", "Carol White", "carol@example.com", "Platinum", DateTimeOffset.UtcNow)
    ];

    static readonly ProductUpdated[] Products = [
        new("prod-1", "Widget Pro", "Electronics", 99.99m, DateTimeOffset.UtcNow),
        new("prod-2", "USB Cable", "Accessories", 14.99m, DateTimeOffset.UtcNow),
        new("prod-3", "Laptop Stand", "Furniture", 135.00m, DateTimeOffset.UtcNow)
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // The processor subscribes from the latest position; give it a moment to attach.
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        var i = 0;

        while (!stoppingToken.IsCancellationRequested) {
            // Reference data first, and again every ten orders so a late subscriber still builds its lookup tables.
            if (i % 10 == 0)
                await SeedReferenceData(stoppingToken);

            var customer = Customers[i % Customers.Length];
            var items    = Products
                .Take(1 + i % Products.Length)
                .Select(p => new OrderItem(p.ProductId, 1 + i % 3, p.CurrentPrice))
                .ToList();

            var order  = new OrderPlaced($"ord-{i:D4}", customer.CustomerId, items, DateTimeOffset.UtcNow);
            var stream = $"order-{order.OrderId}";

            await client.Streams.AppendStream(stream, [AppendRecord.Create(order, r => r with { Stream = stream })], stoppingToken);

            logger.LogInformation("[seed] {OrderId} by {CustomerId} with {Items} item(s)", order.OrderId, order.CustomerId, items.Count);

            i++;

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    async Task SeedReferenceData(CancellationToken ct) {
        foreach (var customer in Customers) {
            var stream = $"customer-{customer.CustomerId}";
            await client.Streams.AppendStream(stream, [AppendRecord.Create(customer, r => r with { Stream = stream })], ct);
        }

        foreach (var product in Products) {
            var stream = $"product-{product.ProductId}";
            await client.Streams.AppendStream(stream, [AppendRecord.Create(product, r => r with { Stream = stream })], ct);
        }

        logger.LogInformation("[seed] Reference data: {Customers} customers, {Products} products", Customers.Length, Products.Length);
    }
}
