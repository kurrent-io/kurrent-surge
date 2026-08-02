using System.Text;
using Kurrent.Quack;
using Kurrent.Projectors.Transactional;

namespace Kurrent.Projectors.DuckDB.Examples;

public static class StringExtensions {
    public static byte[] GetBytes(this string source) =>
        Encoding.UTF8.GetBytes(source);

    public static ReadOnlySpan<byte> GetBytes(this ReadOnlySpan<char> source) {
        Span<byte> span = stackalloc byte[source.Length];
        Encoding.UTF8.GetBytes(source, span);
        return span.ToArray();
    }
}

/// <summary>
/// <see cref="DuckDBAppendProjection{TRow,TBinder}"/>: insert-only, one table, bound by position through
/// the appender. Pick this shape for a log-like table where nothing is ever updated or read back —
/// handlers never even see the connection.
/// </summary>
public class SchemaEventLogProjection
    : DuckDBAppendProjection<SchemaEventLogProjection.LogRow, SchemaEventLogProjection.LogRowBinder> {
    public SchemaEventLogProjection() {
        Setup(connection => {
            using var command = connection.CreateCommand();

            command.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_event_log (
                    schema_name    TEXT NOT NULL,
                    event_type     TEXT NOT NULL,
                    version_number INT  NOT NULL
                );
                """;

            command.ExecuteNonQuery();
        });

        Project<SchemaCreated>((msg, _) =>
            new LogRow(msg.SchemaName, nameof(SchemaCreated), msg.VersionNumber));

        Project<SchemaVersionRegistered>((msg, _) =>
            new LogRow(msg.SchemaName, nameof(SchemaVersionRegistered), msg.VersionNumber));
    }

    protected override string Table => "schema_event_log";

    public readonly record struct LogRow(string SchemaName, string EventType, int VersionNumber);

    public readonly struct LogRowBinder
        : IRowBinder<LogRow, Kurrent.Quack.Appender, Kurrent.Quack.Appender.Row> {
        public static Kurrent.Quack.Appender.Row Bind(in LogRow row, Kurrent.Quack.Appender source) =>
            new(source) {
                row.SchemaName,
                row.EventType,
                row.VersionNumber,
            };
    }
}
