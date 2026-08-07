namespace Kurrent.Projectors.DuckDB.Examples;

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

        Project<SchemaVersionRegistered>(
            msg => msg.SchemaVersionId,
            (msg, current, _) => current is null
                ? null
                : current with { SchemaName = msg.SchemaName, VersionNumber = msg.VersionNumber });

        Project<SchemaVersionDeleted>(
            msg => msg.SchemaVersionId,
            (_, _, _) => null);
    }
}
