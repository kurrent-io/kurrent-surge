using System.Text;
using System.Text.Json.Nodes;

namespace Kurrent.Surge.RecordFilters.Tests.Targets;

public sealed record Order {
    public required string         Stream    { get; init; }
    // Defaulted rather than required so the targets that only exercise other columns stay unchanged.
    public byte[] RecordId { get; init; } = new byte[16];
    public required long           Revision  { get; init; }
    public required decimal        Amount    { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    // Nullable so IS NULL has something to be differential about against a nullable SQL column.
    public string? Status { get; init; }

    public Dictionary<string, object?> Tags    { get; init; } = [];
    public JsonNode?                   Payload { get; init; }
}

public static class OrderSchema {
    public static readonly FilterSchema<Order> Default = FilterSchema<Order>.Create(
        schema => schema
            .Scalar(o => o.Stream,                             "stream")
            .Scalar(o => o.RecordId,                           "record_id")
            .Scalar(o => o.Revision,                           "stream_revision", "revision")
            .Scalar(o => o.Status,                             "status")
            .Scalar(o => o.Amount,                             "amount")
            .Scalar(o => o.CreatedAt.ToUnixTimeMilliseconds(), "created_at", "ts")
            .Map(ResolveTag,                                   "tags", "props", "properties")
            .Json(o => o.Payload,                              "payload", "data")
            .Qualifier("order.")
            .TrimPrefix("field_")
    );

    // A null key means the column itself was referenced, not a member of it — `tags IS NULL` rather
    // than `tags['region'] IS NULL`. Returning null here instead of the whole document would make
    // every keyed column read as SQL NULL, which is what the differential suite caught.
    static object? ResolveTag(Order order, string? key) {
        if (key is null) return TagsAsJson(order.Tags);

        return order.Tags.TryGetValue(key, out var value) ? value : null;
    }

    // Shared with the differential test's INSERT so the DB row and the in-memory object hold identical text.
    public static string TagsAsJson(IReadOnlyDictionary<string, object?> tags) {
        var builder = new StringBuilder("{");

        foreach (var (key, value) in tags) {
            if (builder.Length > 1) builder.Append(',');
            builder.Append('"').Append(key).Append("\":\"").Append(value).Append('"');
        }

        return builder.Append('}').ToString();
    }
}
