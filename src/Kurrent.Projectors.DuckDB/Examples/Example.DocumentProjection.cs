namespace Kurrent.Projectors.DuckDB.Examples;

/// <summary>
/// <see cref="DuckDBDocumentProjection{TDocument,TKey}"/>: the framework loads the current
/// <see cref="SchemaVersion"/> before a handler runs and folds every message for the same key within a
/// batch into one load and one write. <see cref="SchemaCreated"/> and <see cref="SchemaVersionRegistered"/>
/// arriving for the same <c>version_id</c> in one batch still only load once and write once; a
/// <see cref="SchemaVersionDeleted"/> handler returning <c>null</c> deletes the row instead, and any
/// message for that key later in the same batch sees <c>null</c> rather than reloading the row that was
/// just deleted. Pick this shape when a later message needs to see what an earlier one already wrote.
/// </summary>
/// <remarks>
/// There is no storage code here and no SQL: the base class creates the table, reads and writes the
/// document as a JSON row, and keeps every statement inside the projection's own transaction.
/// </remarks>
public class SchemaVersionProjection : DuckDBDocumentProjection<SchemaVersion, string> {
    public SchemaVersionProjection() {
        Project<SchemaCreated>(
            msg => msg.SchemaVersionId,
            (msg, _, _) => new() {
                VersionId        = msg.SchemaVersionId,
                SchemaName       = msg.SchemaName,
                VersionNumber    = msg.VersionNumber,
                SchemaDefinition = msg.SchemaDefinition
            });

        // Current state in hand: keep the definition already stored, move the version forward.
        Project<SchemaVersionRegistered>(
            msg => msg.SchemaVersionId,
            (msg, current, _) => current is null
                ? null
                : current with { SchemaName = msg.SchemaName, VersionNumber = msg.VersionNumber });

        // Returning null deletes: the row is removed when the checkpoint commits this batch's tracked changes.
        Project<SchemaVersionDeleted>(
            msg => msg.SchemaVersionId,
            (_, _, _) => null);
    }
}
