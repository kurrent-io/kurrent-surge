using DuckDB.NET.Data;

namespace Kurrent.Projectors.DuckDB.Examples;

/// <summary>
/// <see cref="DuckDBProjection"/>, the escape hatch: raw ADO commands against the connection, several
/// tables, and a read-modify-write on <see cref="SchemaVersionRegistered"/>. Pick this shape when a
/// message needs to touch more than one table, or when the write depends on state that no single upsert
/// statement can express.
/// </summary>
public class SchemaAuditProjection : DuckDBProjection {
    public SchemaAuditProjection() {
        Setup(connection => {
            using var versions = connection.CreateCommand();
            versions.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_versions_ado (
                    version_id        TEXT NOT NULL,
                    schema_name       TEXT NOT NULL,
                    version_number    INT  NOT NULL,
                    schema_definition BLOB NOT NULL,
                    PRIMARY KEY (version_id)
                );
                """;
            versions.ExecuteNonQuery();

            using var audit = connection.CreateCommand();
            audit.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_audit_ado (
                    schema_name TEXT NOT NULL,
                    description TEXT
                );
                """;
            audit.ExecuteNonQuery();
        });

        Project<SchemaCreated>((msg, context) => {
            using var command = context.Client.CreateCommand();

            command.CommandText = """
                INSERT INTO schema_versions_ado (version_id, schema_name, version_number, schema_definition)
                VALUES ($version_id, $schema_name, $version_number, $schema_definition);
                """;

            command.Parameters.Add(new DuckDBParameter("version_id", msg.SchemaVersionId));
            command.Parameters.Add(new DuckDBParameter("schema_name", msg.SchemaName));
            command.Parameters.Add(new DuckDBParameter("version_number", msg.VersionNumber));
            command.Parameters.Add(new DuckDBParameter("schema_definition", msg.SchemaDefinition));
            command.ExecuteNonQuery();

            return ValueTask.CompletedTask;
        });

        // Read-modify-write: only move the version forward when a row already exists for it.
        Project<SchemaVersionRegistered>((msg, context) => {
            using var select = context.Client.CreateCommand();

            select.CommandText = "SELECT version_id FROM schema_versions_ado WHERE version_id = $version_id;";
            select.Parameters.Add(new DuckDBParameter("version_id", msg.SchemaVersionId));

            using (var reader = select.ExecuteReader()) {
                if (!reader.Read())
                    return ValueTask.CompletedTask;
            }

            using var update = context.Client.CreateCommand();

            update.CommandText = """
                UPDATE schema_versions_ado
                SET schema_name = $schema_name, version_number = $version_number
                WHERE version_id = $version_id;
                """;

            update.Parameters.Add(new DuckDBParameter("schema_name", msg.SchemaName));
            update.Parameters.Add(new DuckDBParameter("version_number", msg.VersionNumber));
            update.Parameters.Add(new DuckDBParameter("version_id", msg.SchemaVersionId));
            update.ExecuteNonQuery();

            return ValueTask.CompletedTask;
        });

        Project<SchemaDescriptionUpdated>((msg, context) => {
            using var command = context.Client.CreateCommand();

            command.CommandText = "INSERT INTO schema_audit_ado (schema_name, description) VALUES ($schema_name, $description);";
            command.Parameters.Add(new DuckDBParameter("schema_name", msg.SchemaName));
            command.Parameters.Add(new DuckDBParameter("description", msg.Description));
            command.ExecuteNonQuery();

            return ValueTask.CompletedTask;
        });

        Project<SchemaDeleted>((msg, context) => {
            using var command = context.Client.CreateCommand();

            command.CommandText = "DELETE FROM schema_versions_ado WHERE schema_name = $schema_name;";
            command.Parameters.Add(new DuckDBParameter("schema_name", msg.SchemaName));
            command.ExecuteNonQuery();

            return ValueTask.CompletedTask;
        });
    }
}
