using System.Text;
using Kurrent.Quack;
using Kurrent.Quack.Threading;
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
        : Kurrent.Quack.IBinder<LogRow, BufferedAppender, BufferedAppender.Row> {
        public static BufferedAppender.Row Bind(in LogRow row, BufferedAppender source) =>
            new(source) {
                row.SchemaName,
                row.EventType,
                row.VersionNumber,
            };
    }
}
