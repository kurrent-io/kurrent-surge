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
    public static readonly PropertyMap<Record> Default = PropertyMap<Record>.Build(map =>
        map.Register(r => r.Stream.Value,
                "stream", "stream_name", "rec.stream", "rec.position.stream")
           .Register(r => r.Schema.SchemaName.Value,
                "schema", "schema_name", "event_type", "rec.schema", "rec.schema.name")
           .Register(r => r.Stream.Category,
                "category", "rec.category")
           .Register(r => r.Timestamp,
                "timestamp", "ts", "created_at", "created_ts", "rec.timestamp", "rec.ts", "rec.created_at")
           .Register(r => r.Position.LogPosition.Value,
                "position", "pos", "log_position", "rec.position", "rec.pos", "rec.log_position")
           .Register(r => r.Revision.Value,
                "revision", "stream_revision", "event_number", "rec.revision", "rec.position.streamrevision")
           .Register(r => r.Id,
                "id", "record_id", "rec.id")
           .RegisterKeyed(ResolveMetadata,
                "properties", "props", "metadata", "rec.properties", "rec.props", "rec.metadata")
    );

    static object? ResolveMetadata(Record record, string? key) =>
        key is not null && record.Metadata.TryGetValue<object>(key, out var value)
            ? value
            : null;
}
