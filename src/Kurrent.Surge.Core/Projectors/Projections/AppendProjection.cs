using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors.Transactional;

/// <summary>
/// Opens one row on a backend's appender and writes that row's fields into it. Implemented by a
/// <c>struct</c> and passed as a type argument, never as an instance.
/// </summary>
/// <typeparam name="TRow">The row to write.</typeparam>
/// <typeparam name="TSource">The backend's appender. May be a <c>ref struct</c>.</typeparam>
/// <typeparam name="TWriter">The open row the backend hands back. May be a <c>ref struct</c>.</typeparam>
/// <remarks>
/// <para>
/// <see cref="Bind"/> is <c>static abstract</c> and the row is taken by <c>in</c>, so appending a row
/// costs no allocation and no virtual call: the JIT specialises the append path per
/// <c>(TRow, TBinder)</c> pair and inlines this straight into it. Both writer types allow a
/// <c>ref struct</c> so a backend can write directly into native buffers.
/// </para>
/// <para>
/// The binder is handed the appender rather than an already-open row, and returns the row it opened, so
/// the backend decides how a row begins and the caller only has to close it. That is what lets a binder
/// be a collection initializer over the appender.
/// </para>
/// </remarks>
[PublicAPI]
public interface IRowBinder<TRow, in TSource, out TWriter>
    where TRow : struct
    where TSource : allows ref struct
    where TWriter : allows ref struct {
    static abstract TWriter Bind(in TRow row, TSource source);
}

/// <summary>
/// A backend's bulk-append handle, open for the duration of one <c>OnProcessBatch</c> call.
/// </summary>
/// <typeparam name="TSource">The backend's appender, as a binder sees it.</typeparam>
/// <typeparam name="TWriter">The open row a binder writes into.</typeparam>
/// <remarks>
/// Disposal must flush. Backends whose appenders buffer are required to land those rows inside the
/// enclosing transaction — an appender still holding rows when that transaction ends can land them
/// afterwards, which silently survives a rollback.
/// </remarks>
[PublicAPI]
public interface IProjectionAppender<out TSource, in TWriter> : IDisposable
    where TSource : allows ref struct
    where TWriter : allows ref struct {
    void Append<TRow, TBinder>(in TRow row)
        where TRow : struct
        where TBinder : IRowBinder<TRow, TSource, TWriter>;
}

/// <summary>
/// Insert-only projection over a single table, using the backend's bulk-append path. Handlers map a
/// message to a row — or to nothing — and never touch the client.
/// </summary>
/// <typeparam name="TClient">The backend's client type.</typeparam>
/// <typeparam name="TRow">The row a handler produces. A <c>struct</c>, so rows never hit the heap.</typeparam>
/// <typeparam name="TSource">The backend's appender. May be a <c>ref struct</c>.</typeparam>
/// <typeparam name="TWriter">The open row a binder writes into. May be a <c>ref struct</c>.</typeparam>
/// <typeparam name="TBinder">Opens a <typeparamref name="TWriter"/> on the appender and writes a <typeparamref name="TRow"/> into it.</typeparam>
/// <remarks>
/// <para>
/// The restrictions are what make it fast: one table, inserts only, no read-back. A column the database
/// computes cannot be skipped the way an upsert skips it, so documents with computed columns belong on
/// <see cref="DocumentProjection{TClient,TDocument,TKey}"/> instead.
/// </para>
/// <para>
/// The appender is opened and disposed inside <see cref="OnProcessBatch"/>, and must stay that way. A backend
/// appender joins the enclosing transaction only if it is disposed before that transaction commits or
/// rolls back — left open across a rollback, the rollback reports success and disposing the appender
/// afterwards flushes the buffered rows in anyway, silently. Scoping it with <c>using</c> here is what
/// makes the failure path safe: it disposes as the exception unwinds, strictly before the caller rolls
/// back. Hoisting it to a field, or to a lifetime that spans batches, breaks exactly-once without
/// breaking anything visible.
/// </para>
/// </remarks>
[PublicAPI]
public abstract class AppendProjection<TClient, TRow, TSource, TWriter, TBinder> : ProjectionProcessor<TClient>
    where TRow : struct
    where TSource : allows ref struct
    where TWriter : allows ref struct
    where TBinder : IRowBinder<TRow, TSource, TWriter> {
    readonly HandlerRegistry<IRowMapper> _handlers = new();

    /// <summary>The target table. Must already exist — create it in <c>Setup</c>.</summary>
    protected abstract string Table { get; }

    /// <summary>Opens the backend's bulk-append handle for <paramref name="table"/>.</summary>
    protected abstract IProjectionAppender<TSource, TWriter> OpenAppender(TClient client, string table);
    
    /// <summary>Maps a message to a row. Returning <c>null</c> appends nothing for that message.</summary>
    protected void Project<T>(Func<T, ProjectionContext<TClient>, TRow?> map) =>
        _handlers.Add<T>(new RowMapper<T>(map));

    /// <inheritdoc/>
    public sealed override ValueTask OnProcessBatch(TClient client, BatchContext context) {
        using var appender = OpenAppender(client, Table);

        foreach (var record in context.Records) {
            var (handler, isFallback, found) = _handlers.Resolve(record);

            if (handler is not null
             && handler.Map(ProjectionContext<TClient>.From(client, record, context)) is { } row)
                appender.Append<TRow, TBinder>(in row);

            // Tracked after each record. A throw disposes the appender during unwind, which flushes
            // the appended prefix into the open transaction — so the frontier equals the flushed
            // rows, and the final checkpoint commits that consistent prefix.
            context.Track(record);
        }

        return ValueTask.CompletedTask;
    }

    #region ->> Handler Types <<-

    interface IRowMapper {
        TRow? Map(ProjectionContext<TClient> context);
    }

    sealed class RowMapper<T>(Func<T, ProjectionContext<TClient>, TRow?> map) : IRowMapper {
        public TRow? Map(ProjectionContext<TClient> context) =>
            map(context.Record.As<T>(), context);
    }

    #endregion
}
