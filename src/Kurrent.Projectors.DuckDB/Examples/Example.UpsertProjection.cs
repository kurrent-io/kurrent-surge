using DuckDB.NET.Data;
using Kurrent.Quack;

namespace Kurrent.Projectors.DuckDB.Examples;

/// <summary>
/// <see cref="DuckDBUpsertProjection{TDocument}"/>: no load, no coalescing — every message maps straight
/// to a full replacement <see cref="SchemaVersion"/>, saved immediately, and the upsert's
/// <c>ON CONFLICT</c> clause is the only merge that happens. Pick this shape when a message always
/// carries the document's complete state, so there is nothing for a load to add and nothing to gain from
/// folding writes by key.
/// </summary>
public class SchemaVersionUpsertProjection : DuckDBUpsertProjection<SchemaVersion> {
    public SchemaVersionUpsertProjection() {
        Setup(connection => {
            using var command = connection.CreateCommand();

            command.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_versions_blind (
                    version_id        TEXT NOT NULL,
                    schema_name       TEXT NOT NULL,
                    version_number    INT  NOT NULL,
                    schema_definition BLOB NOT NULL,
                    PRIMARY KEY (version_id)
                );
                """;

            command.ExecuteNonQuery();
        });

        Project<SchemaCreated>((msg, _) => new SchemaVersion {
            VersionId        = msg.SchemaVersionId,
            SchemaName       = msg.SchemaName,
            VersionNumber    = msg.VersionNumber,
            SchemaDefinition = msg.SchemaDefinition
        });
    }

    protected override async ValueTask Upsert(
        DuckDBAdvancedConnection connection, SchemaVersion document, CancellationToken cancellationToken
    ) {
        using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO schema_versions_blind (version_id, schema_name, version_number, schema_definition)
            VALUES ($version_id, $schema_name, $version_number, $schema_definition)
            ON CONFLICT (version_id) DO UPDATE SET
                schema_name       = EXCLUDED.schema_name,
                version_number    = EXCLUDED.version_number,
                schema_definition = EXCLUDED.schema_definition;
            """;

        command.Parameters.Add(new DuckDBParameter("version_id", document.VersionId));
        command.Parameters.Add(new DuckDBParameter("schema_name", document.SchemaName));
        command.Parameters.Add(new DuckDBParameter("version_number", document.VersionNumber));
        command.Parameters.Add(new DuckDBParameter("schema_definition", document.SchemaDefinition));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
