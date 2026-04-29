using System.Text.Json.Nodes;

namespace Kurrent.Client.Streams;

/// <summary>
/// Pre-built <see cref="PropertyMap{T}"/> for <see cref="Record"/> with the standard column aliases
/// used by expression filters and partition key extractors.
/// </summary>
[PublicAPI]
public static class RecordPropertyMap {
    /// <summary>
    /// The standard property map for <see cref="Record"/> instances.
    /// </summary>
    public static readonly PropertyMap<Record> Default = PropertyMap<Record>.Build(
        configure: map => map
            .Register(
                accessor: r => r.Id,
                "record_id")
            .Register(
                accessor: r => r.Position.LogPosition.Value,
                "log_position", "position", "pos")
            .Register(
                accessor: r => r.Stream.Category,
                "category")
            .Register(
                accessor: r => r.Stream.Value,
                "stream")
            .Register(
                accessor: r => r.Revision.Value,
                "stream_revision", "revision")
            .Register(
                accessor: r => r.Schema.SchemaName.Value,
                "schema_name", "schema")
            .Register(
                accessor: r => r.Schema.SchemaVersionId.Value,
                "schema_id")
            .Register(
                accessor: r => r.Schema.SchemaFormat,
                "schema_format")
            .Register(
                accessor: r => new DateTimeOffset(r.Timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                "created_at", "timestamp", "ts")
            .RegisterKeyed(
                ResolveData,
                "data")
            .RegisterKeyed(
                ResolveMetadata,
                "metadata", "properties", "props")
    );

    static object? ResolveData(Record record, string? key) {
        if (key is null) return null;

        var node = record.DecodedValueAsJson;
        if (node is null) return null;

        // navigate dot-separated path: "address.city" → node["address"]?["city"]
        foreach (var segment in key.Split('.'))
            node = node?[segment];

        return node switch {
            JsonValue v => v.ToString(),
            _           => node?.ToJsonString()
        };
    }

    static object? ResolveMetadata(Record record, string? key) =>
        key is not null && record.Metadata.TryGetValue<object>(key, out var value)
            ? value
            : null;
}
