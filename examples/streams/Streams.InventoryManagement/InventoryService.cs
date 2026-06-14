// ReSharper disable RedundantSwitchExpressionArms

using Kurrent.Client;
using Kurrent.Client.Streams;
using Streams.InventoryManagement.Events;

namespace Streams.InventoryManagement;

/// <summary>
/// Inventory service built on KurrentDB streams.
/// Each SKU gets its own event stream (inventory-{sku}), and the current stock level
/// is computed by folding all events in the stream.
/// </summary>
public sealed class InventoryService(KurrentClient client, ILogger<InventoryService> logger) {
    /// <summary>
    /// Record incoming stock. Simple append with no concurrency check.
    /// </summary>
    public async ValueTask<Revision> ReceiveStock(
        string sku, int quantity, string warehouse, string purchaseOrderId
    ) {
        var result = await client.Streams.AppendStream(
            AppendRecord.New()
                .Stream(StreamFor(sku))
                .Value(new StockReceived(sku, quantity, warehouse, purchaseOrderId, DateTimeOffset.UtcNow))
                .Build());

        logger.LogStockReceived(quantity, sku, warehouse, purchaseOrderId, result.Revision);

        return result.Revision;
    }

    /// <summary>
    /// Ship stock for an order. Reads current level first and uses optimistic concurrency
    /// to prevent overselling. Throws if insufficient stock or if the stream was modified
    /// between the read and the write.
    /// </summary>
    public async ValueTask<Revision> ShipStock(string sku, int quantity, string orderId) {
        var (currentLevel, revision) = await GetStockLevel(sku);

        if (currentLevel < quantity)
            throw new InvalidOperationException(
                $"Cannot ship {quantity} units of {sku}: only {currentLevel} in stock.");

        var stream = StreamFor(sku);
        var record = AppendRecord.New()
            .Stream(stream)
            .Value(new StockShipped(sku, quantity, orderId, DateTimeOffset.UtcNow))
            .Build();

        var result = await client.Streams.AppendStream(stream, [record], revision);

        logger.LogStockShipped(quantity, sku, orderId, result.Revision);

        return result.Revision;
    }

    /// <summary>
    /// Record a manual stock adjustment (e.g. damaged goods, recount).
    /// Uses ExpectedStreamState.Exists to ensure the SKU stream has been created.
    /// </summary>
    public async ValueTask<Revision> AdjustStock(string sku, int quantity, string reason) {
        var stream = StreamFor(sku);
        var record = AppendRecord.New()
            .Stream(stream)
            .Value(new StockAdjusted(sku, quantity, reason, DateTimeOffset.UtcNow))
            .Build();

        var result = await client.Streams.AppendStream(stream, [record], ExpectedStreamState.Exists);

        logger.LogStockAdjusted(sku, quantity, reason, result.Revision);

        return result.Revision;
    }

    /// <summary>
    /// Transfer stock between warehouses as a single atomic operation.
    /// Writes to both the SKU's inventory stream and a cross-cutting transfer-log
    /// stream in one transaction, ensuring both succeed or neither does.
    /// </summary>
    public async ValueTask TransferStock(
        string sku, int quantity, string fromWarehouse, string toWarehouse
    ) {
        var transferEvent = new StockTransferred(
            sku, quantity, fromWarehouse, toWarehouse, DateTimeOffset.UtcNow);

        var skuStream = StreamFor(sku);

        await client.Streams.AppendRecords(
            new[] {
                AppendRecord.New().Stream(skuStream).Value(transferEvent).Build(),
                AppendRecord.New().Stream("transfer-log").Value(transferEvent).Build()
            },
            new AppendRecordsOptions().AddChecks(ConsistencyCheck.StreamExists(skuStream)),
            CancellationToken.None);

        logger.LogStockTransferred(quantity, sku, fromWarehouse, toWarehouse);
    }

    /// <summary>
    /// Compute the current stock level by reading all events and folding them
    /// into a running total. Returns the level and the stream's current revision
    /// (for use in optimistic concurrency checks).
    /// </summary>
    public async ValueTask<(int Level, Revision Revision)> GetStockLevel(string sku) {
        var level    = 0;
        var revision = Revision.From(0);

        await foreach (var message in client.Streams.ReadStream(StreamFor(sku))) {
            if (message.Value is Record record) {
                revision = record.Revision;
                level += record.Value switch {
                    StockReceived e  => e.Quantity,
                    StockShipped e   => -e.Quantity,
                    StockAdjusted e  => e.Quantity,
                    StockTransferred => 0,
                    _                => 0
                };
            }
        }

        return (level, revision);
    }

    /// <summary>
    /// Read the full event history for a SKU as a list.
    /// </summary>
    public ValueTask<List<Record>> GetHistory(string sku) => 
        client.Streams.ReadStream(StreamFor(sku)).ToListAsync();

    /// <summary>
    /// Get the first event in a SKU stream (when this SKU was first tracked).
    /// </summary>
    public ValueTask<Record> GetFirstEvent(string sku) => 
        client.Streams.FirstRecord(StreamFor(sku));

    /// <summary>
    /// Get the most recent event in a SKU stream.
    /// </summary>
    public ValueTask<Record> GetLatestEvent(string sku) => 
        client.Streams.LastRecord(StreamFor(sku));

    /// <summary>
    /// Read events from a known revision onwards — catch-up from a checkpoint.
    /// </summary>
    public async ValueTask<List<Record>> GetEventsSince(string sku, Revision fromRevision) {
        return await client.Streams.ReadStream(StreamFor(sku), fromRevision)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync();
    }

    /// <summary>
    /// Read the last N events from a SKU stream backwards.
    /// </summary>
    public async ValueTask<List<Record>> GetRecentEvents(string sku, int count = 10) {
        var options = new ReadStreamOptions {
            Start     = Revision.Max,
            Direction = ReadDirection.Backwards,
            Limit     = count
        };

        return await client.Streams.ReadStream(StreamFor(sku), options)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync();
    }

    /// <summary>
    /// Read recent inventory activity across all SKUs from the global log.
    /// </summary>
    public async ValueTask<List<Record>> GetRecentActivity(int maxRecords = 50) {
        var options = new ReadRecordsOptions {
            Direction = ReadDirection.Backwards,
            Limit     = maxRecords,
            Filter    = RecordFilter.ByExpression("stream like 'inventory-%'")
        };

        return await client.Streams.ReadRecords(options).ToListAsync();
    }

    static StreamName StreamFor(string sku) => $"inventory-{sku}";
}

static partial class InventoryServiceLogExtensions {
    [LoggerMessage(Level = LogLevel.Information, Message = "Received {Quantity} units of {Sku} at {Warehouse} (PO: {PO}) | revision {Revision}")]
    public static partial void LogStockReceived(this ILogger logger, int quantity, string sku, string warehouse, string po, Revision revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipped {Quantity} units of {Sku} for order {OrderId} | revision {Revision}")]
    public static partial void LogStockShipped(this ILogger logger, int quantity, string sku, string orderId, Revision revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Adjusted {Sku} by {Quantity} ({Reason}) | revision {Revision}")]
    public static partial void LogStockAdjusted(this ILogger logger, string sku, int quantity, string reason, Revision revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transferred {Quantity} units of {Sku} from {From} to {To}")]
    public static partial void LogStockTransferred(this ILogger logger, int quantity, string sku, string from, string to);
}
