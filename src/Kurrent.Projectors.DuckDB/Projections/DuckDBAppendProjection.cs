using System.Text;

using Kurrent.Projectors.Transactional;
using Kurrent.Quack;
using Kurrent.Quack.Threading;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors.DuckDB;

[PublicAPI]
public abstract class DuckDBAppendProjection<TRow, TBinder>
    : ProjectionProcessor<DuckDBAdvancedConnection>, IAsyncDisposable
    where TRow : struct
    where TBinder : IBinder<TRow, BufferedAppender, BufferedAppender.Row>, allows ref struct {
    readonly HandlerRegistry<IRowMapper> _handlers = new();

    IProjectionCheckpointStore<DuckDBAdvancedConnection> _checkpoints = null!;

    DuckDBAdvancedConnection? _connection;
    BufferedAppender?         _appender;

    protected abstract string Table { get; }

    protected void Project<T>(Func<T, ProjectionContext<DuckDBAdvancedConnection>, TRow?> map) =>
        _handlers.Add<T>(new RowMapper<T>(map));

    public override void Wire(
        string projectionId,
        IProjectionClientProvider<DuckDBAdvancedConnection> clients,
        IProjectionCheckpointStore<DuckDBAdvancedConnection> checkpoints
    ) {
        base.Wire(projectionId, clients, checkpoints);

        _checkpoints = checkpoints;
    }

    public sealed override async ValueTask OnBatch(BatchContext context) {
        var appender = await Appender(context.CancellationToken).ConfigureAwait(false);

        foreach (var record in context.Records) {
            var (handler, isFallback, found) = _handlers.Resolve(record);

            if (handler is not null
             && handler.Map(ProjectionContext<DuckDBAdvancedConnection>.From(Clients, record, context)) is { } row)
                appender.Append<TRow, TBinder>(in row);

            context.Track(record);
        }
    }

    protected override async ValueTask OnCheckpoint(CheckpointContext context) {
        if (_appender is null || _connection is null)
            return;

        _appender.Flush();

        await _checkpoints
            .Store(_connection, context.Positions, context.CancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() {
        _appender?.Dispose();

        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);

        _appender   = null;
        _connection = null;
    }

    async ValueTask<BufferedAppender> Appender(CancellationToken cancellationToken) {
        if (_appender is not null)
            return _appender;

        _connection = await Clients.GetClient(cancellationToken).ConfigureAwait(false);

        return _appender = new BufferedAppender(_connection, NullTerminatedUtf8(Table));
    }

    static byte[] NullTerminatedUtf8(string value) {
        var buffer = new byte[Encoding.UTF8.GetByteCount(value) + 1];
        Encoding.UTF8.GetBytes(value, buffer);
        return buffer;
    }

    #region ->> Handler Types <<-

    interface IRowMapper {
        TRow? Map(ProjectionContext<DuckDBAdvancedConnection> context);
    }

    sealed class RowMapper<T>(Func<T, ProjectionContext<DuckDBAdvancedConnection>, TRow?> map) : IRowMapper {
        public TRow? Map(ProjectionContext<DuckDBAdvancedConnection> context) =>
            map(context.Record.As<T>(), context);
    }

    #endregion
}
