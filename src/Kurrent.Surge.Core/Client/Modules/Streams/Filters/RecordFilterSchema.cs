using Kurrent.Surge.RecordFilters;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// The columns a filter expression may reference on a <see cref="Record"/>. Canonical names match the
/// server's index table, so an expression means the same thing evaluated here and sent there.
/// </summary>
/// <remarks>
/// A user-defined index is reached through <see cref="RecordFilter.ByUserIndex"/>, not through an
/// expression: it selects a whole index rather than constraining a column, so it has no column here.
/// </remarks>
static class RecordFilterSchema {
    public static readonly FilterSchema<Record> Default = FilterSchema<Record>.Create(
        schema => schema
            // The server stores record_id as a BLOB, so the column presents the 16 bytes. Every SQL
            // face of it then follows from DuckDB's own functions — hex(record_id) for the hex form,
            // CAST(record_id AS UUID) for the GUID one — with no id-specific handling in the engine.
            .Scalar(r => r.Id.ToByteArray(), "record_id")
            .Scalar(r => r.Position.LogPosition.Value,          "log_position", "position", "pos")
            .Scalar(r => r.Stream.Category,                     "category")
            .Scalar(r => r.Stream.Value,                        "stream")
            .Scalar(r => r.Revision.Value,                      "stream_revision", "revision")
            .Scalar(r => r.Schema.Schema.Value,                 "schema_name", "schema")
            .Scalar(r => r.Schema.VersionId.Value,              "schema_id")
            .Scalar(r => r.Schema.DataFormat,                   "schema_format")
            .Scalar(r => new DateTimeOffset(r.Timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                                                                "created_at", "timestamp", "ts")
            .Json(r => r.DecodedValueAsJson,                    "data")
            .Map(ResolveMetadata,                               "metadata", "properties", "props")
    );

    static object? ResolveMetadata(Record record, string? key) {
        // The bag always exists — Record.Metadata defaults to empty — so a bare `metadata`
        // reference is never SQL NULL. It corresponds to a `{}` column, not a missing one.
        return key is null
            ? record.Metadata
            : record.Metadata.TryGetValue<object>(key, out var value)
                ? value
                : null;
    }
}
