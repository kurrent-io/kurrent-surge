using Kurrent.Projectors.Transactional;
using Kurrent.Quack;

namespace Kurrent.Projectors.DuckDB;

/// <summary>
/// Adapts Quack's <see cref="Kurrent.Quack.Appender"/> onto <see cref="IProjectionAppender{TWriter}"/>,
/// with <see cref="Kurrent.Quack.Appender.Row"/> as the row writer.
/// </summary>
/// <remarks>
/// <para>
/// The binder opens the row off the appender and hands it back, and disposing it is what ends the row, so
/// the return value goes straight into <see cref="Kurrent.Quack.Appender.Row.Dispose"/>.
/// </para>
/// <para>
/// Quack's appender does not flush when it is destroyed — buffered rows are lost — so <see cref="Dispose"/>
/// flushes first. That flush must land before the enclosing transaction ends, which is what
/// <see cref="AppendProjection{TClient,TRow,TSource,TWriter,TBinder}.OnProcessBatch"/> guarantees by scoping
/// the appender with <c>using</c>: rows flushed inside the transaction are discarded by a rollback, rows
/// flushed after it survive one.
/// </para>
/// <para>
/// The appender type is spelled out fully qualified in signatures: <c>Kurrent.Appender</c> is a namespace
/// this solution also ships, and ancestor-namespace lookup finds it before the <c>Kurrent.Quack</c> import.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class DuckDBRowAppender : IProjectionAppender<Kurrent.Quack.Appender, Kurrent.Quack.Appender.Row> {
    /// <param name="connection">The connection whose transaction the appended rows join.</param>
    /// <param name="tableNameUtf8NullTerminated">The target table, as a UTF-8 C string.</param>
    public DuckDBRowAppender(DuckDBAdvancedConnection connection, ReadOnlySpan<byte> tableNameUtf8NullTerminated) =>
        _appender = new Kurrent.Quack.Appender(connection, tableNameUtf8NullTerminated);

    Kurrent.Quack.Appender _appender;

    public void Append<TRow, TBinder>(in TRow row)
        where TRow : struct
        where TBinder : IRowBinder<TRow, Kurrent.Quack.Appender, Kurrent.Quack.Appender.Row> =>
        TBinder.Bind(in row, _appender).Dispose();

    public void Dispose() {
        try {
            _appender.Flush();
        } finally {
            _appender.Dispose();
        }
    }
}
