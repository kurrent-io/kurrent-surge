# Processor.OrderEnrichment

Stream-table join pattern for enriching orders with customer and product details.

## What You'll Learn

- Building lookup tables from reference streams
- Stream-table join pattern (enrichment)
- Processing multiple event types
- Handling missing reference data gracefully

## How It Works

1. **Input**: Three event types:
   - `CustomerUpdated` → Build customer lookup table
   - `ProductUpdated` → Build product lookup table
   - `OrderPlaced` → Enrich and process
2. **Processing**:
   - Maintain in-memory lookup tables for customers and products
   - When an order arrives, join with lookup tables
   - Add customer name, tier, product names, categories
3. **Output**: Log enriched orders with full details

```
CustomerUpdated(cust1, "Alice Smith", "Gold")
    → Lookup table: {cust1: {name: "Alice Smith", tier: "Gold"}}

ProductUpdated(prod1, "Widget Pro", "Electronics", $99.99)
    → Lookup table: {prod1: {name: "Widget Pro", category: "Electronics"}}

OrderPlaced(cust1, [{prod1, qty: 2}])
    → 📦 ORDER | Customer: Alice Smith (Gold) | 2x Widget Pro | Total: $199.98
    → 🎁 Gold tier discount: 10% (saves $19.99)
```

## Prerequisites

- KurrentDB running on `localhost:2113`

## Running the Example

```bash
cd examples/processors/Processor.OrderEnrichment
dotnet run
```

A hosted `OrderSeeder` writes the customer and product reference data first, then one order per
second, so every order arrives fully enriched. The processor subscribes to the `customer-*`,
`product-*` and `order-*` streams by prefix. Press `Ctrl+C` to stop.

## Key Code

```csharp
void EnrichAndLogOrder(OrderPlaced order, ILogger logger) {
    // Join with customer lookup
    var customer = _customers.GetValueOrDefault(order.CustomerId);
    var customerName = customer?.Name ?? $"Unknown ({order.CustomerId})";

    // Join with product lookups
    foreach (var item in order.Items) {
        var product = _products.GetValueOrDefault(item.ProductId);
        enrichedItems.Add(new EnrichedOrderItem(
            ProductName: product?.Name ?? "Unknown",
            Category: product?.Category ?? "Unknown",
            // ...
        ));
    }
}
```

## Tier Discounts

| Tier | Discount |
|------|----------|
| Platinum | 15% |
| Gold | 10% |
| Silver | 5% |
| Bronze | 0% |

## Sample Output

```
[INF] Building lookup tables from CustomerUpdated and ProductUpdated events
[INF] 📦 ORDER ord-123 | Customer: Alice Smith (Gold) | Items: 3 | Total: $349.97 ✓
[INF]    └─ 2x Widget Pro (Electronics) @ $99.99 = $199.98
[INF]    └─ 1x USB Cable (Accessories) @ $14.99 = $14.99
[INF]    └─ 1x Laptop Stand (Furniture) @ $135.00 = $135.00
[INF]    └─ 🎁 Gold tier discount: 10% (saves $35.00)
[INF] Checkpoint #1 | Orders: 45 (42 fully enriched) | Customers: 128 | Products: 567
```

## Original Tutorials

- [Confluent Multi-Join Tutorial](https://developer.confluent.io/confluent-tutorials/multi-joins/flinksql/)
- [Flink Table API Joins](https://nightlies.apache.org/flink/flink-docs-stable/docs/dev/table/sql/queries/joins/)
