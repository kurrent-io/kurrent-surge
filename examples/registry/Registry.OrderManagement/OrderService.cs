using Kurrent.Client;
using Kurrent.Client.Streams;
using Registry.OrderManagement.Events;

namespace Registry.OrderManagement;

public sealed class OrderService(KurrentClient client, ILogger<OrderService> logger) {
    public async ValueTask<Revision> PlaceOrder(OrderPlaced order) {
        var result = await client.Streams.AppendStream(
            StreamFor(order.OrderId),
            [AppendRecord.New().Value(order).Build()]);

        logger.LogOrderPlaced(order.OrderId, result.Revision);
        return result.Revision;
    }

    public async ValueTask<Revision> ShipOrder(OrderShipped shipped) {
        var result = await client.Streams.AppendStream(
            StreamFor(shipped.OrderId),
            [AppendRecord.New().Value(shipped).Build()],
            ExpectedStreamState.Exists);

        logger.LogOrderShipped(shipped.OrderId, shipped.TrackingNumber, result.Revision);
        return result.Revision;
    }

    public async ValueTask<List<EventInfo>> GetEvents(string orderId) {
        var events = new List<EventInfo>();

        await foreach (var msg in  client.Streams.ReadStreamRecords(StreamFor(orderId))) {
            msg.Switch(
                record => events.Add(new EventInfo(
                    record.Revision.Value,
                    record.Schema.SchemaName.Value,
                    record.Schema.SchemaFormat.ToString(),
                    record.Schema.SchemaVersionId.Value.ToString(),
                    record.Timestamp,
                    record.Value?.GetType().Name ?? "unknown"
                )),
                _ => { });
        }

        return events;
    }

    static StreamName StreamFor(string orderId) => $"order-{orderId}";
}

public record EventInfo(
    long Revision,
    string SchemaName,
    string SchemaFormat,
    string SchemaVersionId,
    DateTime Timestamp,
    string ClrType
);

static partial class OrderServiceLogExtensions {
    [LoggerMessage(Level = LogLevel.Information, Message = "Placed order {OrderId} | revision {Revision}")]
    public static partial void LogOrderPlaced(this ILogger logger, string orderId, Revision revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipped order {OrderId} tracking {Tracking} | revision {Revision}")]
    public static partial void LogOrderShipped(this ILogger logger, string orderId, string tracking, Revision revision);
}
