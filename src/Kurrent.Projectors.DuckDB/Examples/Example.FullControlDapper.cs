using DuckDB.NET.Data;

namespace Kurrent.Projectors.DuckDB.Examples;

/// <summary>
/// The same <see cref="DuckDBProjection"/> shape as <see cref="SchemaAuditProjection"/>, but written with
/// concise <c>Execute</c>/<c>QueryFirstOrDefault</c> ergonomics instead of raw <c>DuckDBCommand</c>
/// plumbing. Pick this shape to see that the framework does not lock a user into hand-rolled ADO — any
/// data-access library that can run SQL against a <c>DbConnection</c> works here.
/// </summary>
/// <remarks>
/// <see cref="ConciseSqlConnectionExtensions"/> is a tiny local stand-in so the call shape can be shown
/// without adding a package reference. It binds explicit name/value pairs rather than reading an
/// anonymous object's properties: this package declares <c>IsAotCompatible</c>, and reflection-based
/// parameter binding — which is how Dapper's anonymous-object overloads work — is not AOT-safe. A
/// consumer whose application does not target AOT can drop Dapper in here unchanged.
/// </remarks>
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

        Project<SchemaCreated>((msg, context) => {
            context.Client.Execute(
                """
                INSERT INTO schema_versions_dapper (version_id, schema_name, version_number, schema_definition)
                VALUES ($version_id, $schema_name, $version_number, $schema_definition);
                """,
                ("version_id", msg.SchemaVersionId),
                ("schema_name", msg.SchemaName),
                ("version_number", msg.VersionNumber),
                ("schema_definition", msg.SchemaDefinition));

            return ValueTask.CompletedTask;
        });

        // Read-modify-write: QueryFirstOrDefault instead of a manual reader loop.
        Project<SchemaVersionRegistered>((msg, context) => {
            var exists = context.Client.QueryFirstOrDefault(
                "SELECT version_id FROM schema_versions_dapper WHERE version_id = $version_id;",
                reader => reader.GetString(0),
                ("version_id", msg.SchemaVersionId));

            if (exists is null)
                return ValueTask.CompletedTask;

            context.Client.Execute(
                """
                UPDATE schema_versions_dapper
                SET schema_name = $schema_name, version_number = $version_number
                WHERE version_id = $version_id;
                """,
                ("schema_name", msg.SchemaName),
                ("version_number", msg.VersionNumber),
                ("version_id", msg.SchemaVersionId));

            return ValueTask.CompletedTask;
        });

        Project<SchemaDescriptionUpdated>((msg, context) => {
            context.Client.Execute(
                "INSERT INTO schema_audit_dapper (schema_name, description) VALUES ($schema_name, $description);",
                ("schema_name", msg.SchemaName),
                ("description", msg.Description));

            return ValueTask.CompletedTask;
        });
    }
}

/// <summary>
/// Minimal SQL-with-named-parameters ergonomics over <see cref="DuckDBConnection"/> — enough to
/// demonstrate the call shape without a package reference. Parameters are supplied as explicit
/// name/value pairs matching this project's <c>$name</c> convention.
/// </summary>
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
