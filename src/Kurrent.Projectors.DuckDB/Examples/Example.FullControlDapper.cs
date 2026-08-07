using DuckDB.NET.Data;

namespace Kurrent.Projectors.DuckDB.Examples;

public class SchemaAuditDapperProjection : DuckDBProjection {
    public SchemaAuditDapperProjection() {
        Setup(connection => {
            connection.Execute("""
                CREATE TABLE IF NOT EXISTS schema_versions_dapper (
                    version_id        TEXT NOT NULL,
                    schema_name       TEXT NOT NULL,
                    version_number    INT  NOT NULL,
                    schema_definition BLOB NOT NULL,
                    PRIMARY KEY (version_id)
                );
                """);

            connection.Execute("""
                CREATE TABLE IF NOT EXISTS schema_audit_dapper (
                    schema_name TEXT NOT NULL,
                    description TEXT
                );
                """);
        });

        Project<SchemaCreated>(async (msg, context) => {
            var connection = await context.Clients.GetClient(context.CancellationToken);

            connection.Execute(
                """
                INSERT INTO schema_versions_dapper (version_id, schema_name, version_number, schema_definition)
                VALUES ($version_id, $schema_name, $version_number, $schema_definition);
                """,
                ("version_id", msg.SchemaVersionId),
                ("schema_name", msg.SchemaName),
                ("version_number", msg.VersionNumber),
                ("schema_definition", msg.SchemaDefinition));
        });

        Project<SchemaVersionRegistered>(async (msg, context) => {
            var connection = await context.Clients.GetClient(context.CancellationToken);

            var exists = connection.QueryFirstOrDefault(
                "SELECT version_id FROM schema_versions_dapper WHERE version_id = $version_id;",
                reader => reader.GetString(0),
                ("version_id", msg.SchemaVersionId));

            if (exists is null)
                return;

            connection.Execute(
                """
                UPDATE schema_versions_dapper
                SET schema_name = $schema_name, version_number = $version_number
                WHERE version_id = $version_id;
                """,
                ("schema_name", msg.SchemaName),
                ("version_number", msg.VersionNumber),
                ("version_id", msg.SchemaVersionId));
        });

        Project<SchemaDescriptionUpdated>(async (msg, context) => {
            var connection = await context.Clients.GetClient(context.CancellationToken);

            connection.Execute(
                "INSERT INTO schema_audit_dapper (schema_name, description) VALUES ($schema_name, $description);",
                ("schema_name", msg.SchemaName),
                ("description", msg.Description));
        });
    }
}

static class ConciseSqlConnectionExtensions {
    public static int Execute(
        this DuckDBConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters
    ) {
        using var command = connection.CreateCommand();

        command.CommandText = sql;
        Bind(command, parameters);

        return command.ExecuteNonQuery();
    }

    public static TResult? QueryFirstOrDefault<TResult>(
        this DuckDBConnection connection,
        string sql,
        Func<DuckDBDataReader, TResult> map,
        params (string Name, object? Value)[] parameters
    ) {
        using var command = connection.CreateCommand();

        command.CommandText = sql;
        Bind(command, parameters);

        using var reader = command.ExecuteReader();

        return reader.Read() ? map(reader) : default;
    }

    static void Bind(DuckDBCommand command, (string Name, object? Value)[] parameters) {
        foreach (var (name, value) in parameters)
            command.Parameters.Add(new DuckDBParameter(name, value ?? DBNull.Value));
    }
}
