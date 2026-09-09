# Streams.QuickStart

Minimal example — append events to a stream and read them back.

## Running the Example

```bash
cd examples/streams/Streams.QuickStart
dotnet run
```

Connects via `ConnectionStrings:kurrentdb` in `appsettings.json`, which defaults to
`localhost:2113`. Point at a different server by editing the file or setting the
`ConnectionStrings__kurrentdb` environment variable.

## Sample Output

```
[INF] Appended 3 events to order-a1b2c3d4 at revision 2
[INF] [0] OrderPlaced: OrderPlaced { OrderId = ORD-001, Customer = Alice, ItemCount = 3, ... }
[INF] [1] OrderPaid: OrderPaid { OrderId = ORD-001, Amount = 29.97, ... }
[INF] [2] OrderShipped: OrderShipped { OrderId = ORD-001, TrackingNumber = TRACK-9876, ... }
```
