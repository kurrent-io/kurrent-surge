using Kurrent.Quack;

namespace Kurrent.Projectors.DuckDB;

[PublicAPI]
public static class DuckDBConnectionExtensions {
    /// <summary>
    /// Returns <c>true</c> if a table with the given name exists in the current database.
    /// Quoted identifiers are matched against their unquoted stored form in <c>information_schema</c>.
    /// </summary>
    public static bool TableExists(this DuckDBAdvancedConnection connection, string tableName) {
        var result = connection.QueryFirstOrDefault<TableExistsArgs, bool, TableExistsQuery>(new(tableName));
        return result is { IsUndefined: false, Value: true };
    }

    readonly record struct TableExistsArgs(string TableName);

    struct TableExistsQuery : IQuery<TableExistsArgs, bool> {
        public static StatementBindingResult Bind(in TableExistsArgs args, PreparedStatement statement) =>
            new(statement) { args.TableName };

        public static ReadOnlySpan<byte> CommandText => """
            SELECT CAST(COUNT(table_name) AS BOOLEAN)
            FROM information_schema.tables
            WHERE table_name = $1;
            """u8;

        public static bool Parse(ref DataChunk.Row row) => row.ReadBoolean();
    }
}
