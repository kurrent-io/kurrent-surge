# Streams.InventoryManagement

A warehouse inventory API built on KurrentDB streams. Tracks stock movements across warehouses — receiving shipments, fulfilling orders, adjusting for damaged goods, and transferring between locations.

## Running the Example

```bash
cd examples/streams/Streams.InventoryManagement
dotnet run
```

Requires KurrentDB running on `localhost:2113`. The API starts on `http://localhost:5000`.

## Try It

### Receive a shipment

```bash
curl -s http://localhost:5000/stock/WIDGET-001/receive \
  -H 'Content-Type: application/json' \
  -d '{"quantity": 100, "warehouse": "Warehouse-A", "purchaseOrderId": "PO-2024-001"}'

curl -s http://localhost:5000/stock/WIDGET-001/receive \
  -H 'Content-Type: application/json' \
  -d '{"quantity": 50, "warehouse": "Warehouse-B", "purchaseOrderId": "PO-2024-002"}'
```

### Check stock level

```bash
curl -s http://localhost:5000/stock/WIDGET-001 | jq
# → { "sku": "WIDGET-001", "level": 150, "revision": 1 }
```

### Ship an order

Uses optimistic concurrency — reads current level, validates, appends with expected revision.

```bash
curl -s http://localhost:5000/stock/WIDGET-001/ship \
  -H 'Content-Type: application/json' \
  -d '{"quantity": 25, "orderId": "ORD-10001"}'

curl -s http://localhost:5000/stock/WIDGET-001 | jq
# → { "sku": "WIDGET-001", "level": 125, "revision": 2 }
```

### Adjust for damaged goods

Requires the stream to already exist (exists guard).

```bash
curl -s http://localhost:5000/stock/WIDGET-001/adjust \
  -H 'Content-Type: application/json' \
  -d '{"quantity": -3, "reason": "Damaged during inspection"}'
```

### Transfer between warehouses

Atomic multi-stream write — updates both the SKU stream and a cross-cutting transfer log in one transaction.

```bash
curl -s http://localhost:5000/stock/WIDGET-001/transfer \
  -H 'Content-Type: application/json' \
  -d '{"quantity": 20, "fromWarehouse": "Warehouse-A", "toWarehouse": "Warehouse-B"}'

curl -s http://localhost:5000/stock/WIDGET-001 | jq
# → level unchanged (warehouse moves are net-zero)
```

### View event history

```bash
# Full audit trail
curl -s http://localhost:5000/stock/WIDGET-001/history | jq

# First and latest events
curl -s http://localhost:5000/stock/WIDGET-001/history/first | jq
curl -s http://localhost:5000/stock/WIDGET-001/history/latest | jq

# Events since revision 2 (catch-up read)
curl -s http://localhost:5000/stock/WIDGET-001/history/since/2 | jq

# Last 3 events (backwards read)
curl -s http://localhost:5000/stock/WIDGET-001/history/recent/3 | jq
```

### Activity across all SKUs

Reads from the global transaction log with a filter expression.

```bash
curl -s http://localhost:5000/activity | jq
curl -s 'http://localhost:5000/activity?max=5' | jq
```

## Endpoints

| Method | Path | Pattern |
|--------|------|---------|
| POST | `/stock/{sku}/receive` | Basic append |
| POST | `/stock/{sku}/ship` | Optimistic concurrency |
| POST | `/stock/{sku}/adjust` | Exists guard |
| POST | `/stock/{sku}/transfer` | Multi-stream atomic |
| GET | `/stock/{sku}` | Fold stream into state |
| GET | `/stock/{sku}/history` | Stream to list |
| GET | `/stock/{sku}/history/first` | First record |
| GET | `/stock/{sku}/history/latest` | Latest record |
| GET | `/stock/{sku}/history/since/{revision}` | Catch-up read |
| GET | `/stock/{sku}/history/recent/{count}` | Backwards read |
| GET | `/activity` | Global log with filter |

## Key Code

### Optimistic concurrency for order fulfillment

```csharp
var (currentLevel, revision) = await GetStockLevel(sku);

var result = await client.Streams.AppendToStream(
    AppendToStreamRequest.For($"inventory-{sku}")
        .ExpectingState(revision)
        .WithRecord(new StockShipped(sku, quantity, orderId, DateTimeOffset.UtcNow))
        .Build());
```

### Atomic multi-stream writes for warehouse transfers

```csharp
await client.Streams.AppendRecords(
    AppendRecordsRequest.New()
        .WithRecord(r => r.ForStream($"inventory-{sku}").WithValue(transferEvent))
        .WithRecord(r => r.ForStream("transfer-log").WithValue(transferEvent))
        .WithChecks(ConsistencyCheck.StreamExists($"inventory-{sku}"))
        .Build());
```

### Folding a stream into current state

```csharp
await using var stream = await client.Streams.ReadStreamRecords($"inventory-{sku}");

await foreach (var message in stream) {
    message.Switch(
        record => {
            level += record.Value switch {
                StockReceived e  => e.Quantity,
                StockShipped e   => -e.Quantity,
                StockAdjusted e  => e.Quantity,
                _                => 0
            };
        },
        _ => { });
}
```
