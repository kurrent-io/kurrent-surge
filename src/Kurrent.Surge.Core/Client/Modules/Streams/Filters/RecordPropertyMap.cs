using System.Text.Json.Nodes;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Pre-built <see cref="PropertyMap{T}"/> for <see cref="Record"/> with the standard column aliases
/// used by expression filters and partition key extractors.
/// </summary>
[PublicAPI]
public static class RecordPropertyMap {
    /// <summary>
    /// The standard property map for <see cref="Record"/> instances.
    /// </summary>
    public static readonly PropertyMap<Record> Default = PropertyMap<Record>.Create(
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
                accessor: r => r.Schema.Schema.Value,
                "schema_name", "schema")
            .Register(
                accessor: r => r.Schema.VersionId.Value,
                "schema_id")
            .Register(
                accessor: r => r.Schema.DataFormat,
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
        var node = record.DecodedValueAsJson;
        if (node is null) return null;

        // A null key means the column itself was referenced — `data IS NULL`, not `data->>'city'`.
        // The answer is the whole document, matching what the server's `data` column holds.
        if (key is null) return node.ToJsonString();

        // navigate dot-separated path: "address.city" → node["address"]?["city"]
        foreach (var segment in key.Split('.'))
            node = node?[segment];

        return node switch {
            JsonValue v => v.ToString(),
            _           => node?.ToJsonString()
        };
    }

    static object? ResolveMetadata(Record record, string? key) {
        // The bag always exists — Record.Metadata defaults to empty — so a bare `metadata`
        // reference is never SQL NULL. It corresponds to a `{}` column, not a missing one.
        if (key is null) return record.Metadata;

        return record.Metadata.TryGetValue<object>(key, out var value) ? value : null;
    }
}
