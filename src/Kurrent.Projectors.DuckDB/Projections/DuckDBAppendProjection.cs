using System.Text;
using Kurrent.Quack;
using Kurrent.Projectors.Transactional;

namespace Kurrent.Projectors.DuckDB;

/// <summary>
/// Closed <see cref="AppendProjection{TClient,TRow,TWriter,TBinder}"/> for Quack's appender, so a
/// projection names only its row and its binder.
/// </summary>
/// <remarks>
/// The base and the appender are spelled out fully qualified: <c>Kurrent.Projectors</c> and
/// <c>Kurrent.Appender</c> are literal ancestors of this file's namespace and ship unrelated types of the
/// same name. Ancestor-namespace lookup wins over a <c>using</c> import before the import is considered,
/// and the symptom of getting it wrong is a confusing arity or namespace error rather than a missing type.
/// </remarks>
[PublicAPI]
public abstract class DuckDBAppendProjection<TRow, TBinder>
    : AppendProjection<DuckDBAdvancedConnection, TRow, Kurrent.Quack.Appender, Kurrent.Quack.Appender.Row, TBinder>
    where TRow : struct
    where TBinder : IRowBinder<TRow, Kurrent.Quack.Appender, Kurrent.Quack.Appender.Row> {
    /// <inheritdoc/>
    protected sealed override IProjectionAppender<Kurrent.Quack.Appender, Kurrent.Quack.Appender.Row> OpenAppender(
        DuckDBAdvancedConnection client, string table
    ) =>
        new DuckDBRowAppender(client, NullTerminatedUtf8(table));

    // duckdb_appender_create reads the table name as a C string. The terminator is the buffer's
    // zero-initialised last byte, since Encoding.UTF8 never writes one.
    static byte[] NullTerminatedUtf8(string value) {
        var buffer = new byte[Encoding.UTF8.GetByteCount(value) + 1];
        Encoding.UTF8.GetBytes(value, buffer);
        return buffer;
    }
}
