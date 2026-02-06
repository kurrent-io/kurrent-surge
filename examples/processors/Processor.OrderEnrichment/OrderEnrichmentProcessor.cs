using System.Collections.Concurrent;
using Kurrent.Client.Streams;
using Kurrent.Processors;
using Microsoft.Extensions.Logging;
using Processor.OrderEnrichment.Events;

namespace Processor.OrderEnrichment;

/// <summary>
/// Order enrichment processor using stream-table joins.
/// Builds lookup tables from reference events and enriches orders on-the-fly.
/// Demonstrates the classic stream-table join pattern.
/// </summary>
public sealed class OrderEnrichmentProcessor : IRecordProcessor {
    // Lookup tables (materialized from reference streams)
    readonly ConcurrentDictionary<string, CustomerInfo> _customers = new();
    readonly ConcurrentDictionary<string, ProductInfo>  _products  = new();

    long _ordersProcessed;
    long _ordersFullyEnriched;
    long _customerUpdates;
    long _productUpdates;

    public ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            switch (record.Value) {
                case CustomerUpdated customer:
                    UpdateCustomerLookup(customer, context.Logger);
                    break;

                case ProductUpdated product:
                    UpdateProductLookup(product, context.Logger);
                    break;

                case OrderPlaced order:
                    EnrichAndLogOrder(order, context.Logger);
                    break;
            }
        }

        return ValueTask.CompletedTask;
    }

    void UpdateCustomerLookup(CustomerUpdated customer, ILogger logger) {
        _customerUpdates++;
        _customers[customer.CustomerId] = new CustomerInfo(
            customer.CustomerId,
            customer.Name,
            customer.Email,
            customer.Tier
        );
        logger.LogDebug("Updated customer lookup: {CustomerId} ({Name})", customer.CustomerId, customer.Name);
    }

    void UpdateProductLookup(ProductUpdated product, ILogger logger) {
        _productUpdates++;
        _products[product.ProductId] = new ProductInfo(
            product.ProductId,
            product.Name,
            product.Category,
            product.CurrentPrice
        );
        logger.LogDebug("Updated product lookup: {ProductId} ({Name})", product.ProductId, product.Name);
    }

    void EnrichAndLogOrder(OrderPlaced order, ILogger logger) {
        _ordersProcessed++;

        // Enrich with customer info
        var customer = _customers.GetValueOrDefault(order.CustomerId);
        var customerName = customer?.Name ?? $"Unknown ({order.CustomerId})";
        var customerTier = customer?.Tier ?? "Unknown";

        // Enrich line items with product info
        var enrichedItems = new List<EnrichedOrderItem>();
        var allProductsFound = true;

        foreach (var item in order.Items) {
            var product = _products.GetValueOrDefault(item.ProductId);
            if (product == null) {
                allProductsFound = false;
            }

            enrichedItems.Add(new EnrichedOrderItem(
                ProductId: item.ProductId,
                ProductName: product?.Name ?? $"Unknown ({item.ProductId})",
                Category: product?.Category ?? "Unknown",
                Quantity: item.Quantity,
                UnitPrice: item.UnitPrice,
                LineTotal: item.Quantity * item.UnitPrice
            ));
        }

        var orderTotal = enrichedItems.Sum(i => i.LineTotal);
        var isFullyEnriched = customer != null && allProductsFound;

        if (isFullyEnriched) {
            _ordersFullyEnriched++;
        }

        // Log the enriched order
        logger.LogInformation(
            "📦 ORDER {OrderId} | Customer: {Customer} ({Tier}) | Items: {ItemCount} | Total: ${Total:F2} {EnrichStatus}",
            order.OrderId,
            customerName,
            customerTier,
            order.Items.Count,
            orderTotal,
            isFullyEnriched ? "✓" : "⚠️ partial"
        );

        foreach (var item in enrichedItems) {
            logger.LogInformation(
                "   └─ {Qty}x {Product} ({Category}) @ ${Price:F2} = ${LineTotal:F2}",
                item.Quantity,
                item.ProductName,
                item.Category,
                item.UnitPrice,
                item.LineTotal
            );
        }

        // Apply tier discount info
        if (customer != null) {
            var discount = GetTierDiscount(customerTier);
            if (discount > 0) {
                logger.LogInformation(
                    "   └─ 🎁 {Tier} tier discount: {Discount}% (saves ${Savings:F2})",
                    customerTier,
                    discount * 100,
                    orderTotal * discount
                );
            }
        }
    }

    static decimal GetTierDiscount(string tier) => tier switch {
        "Platinum" => 0.15m,
        "Gold"     => 0.10m,
        "Silver"   => 0.05m,
        _          => 0m
    };

    public ValueTask OnCheckpoint(CheckpointContext context) {
        context.Logger.LogInformation(
            "Checkpoint {CheckpointId} | Orders: {Orders} ({Enriched} fully enriched) | Customers: {Customers} | Products: {Products}",
            context.CheckpointId,
            _ordersProcessed,
            _ordersFullyEnriched,
            _customers.Count,
            _products.Count
        );

        return ValueTask.CompletedTask;
    }

    public ValueTask OnComplete(CompleteContext context) {
        context.Logger.LogInformation(
            "Completed | Orders: {Orders}, Customer updates: {CustUpdates}, Product updates: {ProdUpdates}",
            _ordersProcessed,
            _customerUpdates,
            _productUpdates
        );
        return ValueTask.CompletedTask;
    }

    record CustomerInfo(string CustomerId, string Name, string Email, string Tier);
    record ProductInfo(string ProductId, string Name, string Category, decimal CurrentPrice);
    record EnrichedOrderItem(string ProductId, string ProductName, string Category, int Quantity, decimal UnitPrice, decimal LineTotal);
}
