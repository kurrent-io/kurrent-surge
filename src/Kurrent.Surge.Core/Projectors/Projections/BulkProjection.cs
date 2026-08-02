using Kurrent.Projectors.Transactional;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors;

/// <summary>
/// Whole-batch projection. One handler receives every record in the batch plus the client and does
/// whatever it likes — including reaching records that were never decoded, which no typed shape can.
/// </summary>
[PublicAPI]
public abstract class BulkProjection<TClient> : ProjectionProcessor<TClient> {
    IBulkProjectionHandler? _handler;

    #region ->> Project <<-

    protected void Project(Func<BulkProjectionContext<TClient>, ValueTask> handler) =>
        _handler = new ProxyHandler(handler);

    protected void Project(Action<BulkProjectionContext<TClient>> handler) =>
        _handler = new ProxyHandler(handler);

    #endregion

    /// <inheritdoc/>
    public sealed override ValueTask OnProcessBatch(TClient client, BatchContext context) =>
        _handler is null
            ? throw new InvalidOperationException(
                $"{GetType().Name} registered no batch handler. Call Project(...) in its constructor.")
            : _handler.Handle(BulkProjectionContext<TClient>.From(client, context));

    #region ->> Handler Types <<-

    public interface IBulkProjectionHandler {
        ValueTask Handle(BulkProjectionContext<TClient> context);
    }

    sealed class ProxyHandler : IBulkProjectionHandler {
        readonly Func<BulkProjectionContext<TClient>, ValueTask> _handler;

        public ProxyHandler(Func<BulkProjectionContext<TClient>, ValueTask> handler) =>
            _handler = handler;

        public ProxyHandler(Action<BulkProjectionContext<TClient>> handler) =>
            _handler = ctx => {
                handler(ctx);
                return ValueTask.CompletedTask;
            };

        public ValueTask Handle(BulkProjectionContext<TClient> context) =>
            _handler(context);
    }

    #endregion
}

/// <summary>
/// Context for a handler that projects a whole batch. The batch is not the commit boundary — the
/// transaction spans every batch until the next checkpoint commits it — so a handler writes and returns
/// without committing anything.
/// </summary>
/// <inheritdoc cref="ProjectionContext{TClient}" path="/remarks"/>
[PublicAPI]
public readonly record struct BulkProjectionContext<TClient> {
    readonly BatchContext _source;

    BulkProjectionContext(TClient client, BatchContext source) {
        Client  = client;
        _source = source;
    }

    public TClient Client { get; }

    public RecordBatch       Records           => _source.Records;
    public string            ProjectionId      => _source.ProcessorId;
    public string            DroneId           => _source.DroneId;
    public SessionId         SessionId         => _source.SessionId;
    public ILogger           Logger            => _source.Logger;
    public CancellationToken CancellationToken => _source.CancellationToken;
    public DroneProgress     Progress          => _source.Progress;

    /// <summary>
    /// Marks one record as durably handled. Call it after applying each record, and the tracked
    /// frontier equals the applied work at every instant.
    /// </summary>
    public void Track(Record record) => _source.Track(record);

    /// <summary>
    /// Marks the batch handled up to and including <paramref name="upTo"/>. Records past it stay pending.
    /// </summary>
    /// <remarks>
    /// This folds the whole batch on every call. Use it for one sparse claim; inside a per-record loop
    /// use <see cref="Track(Record)"/>.
    /// </remarks>
    public void Track(RecordPosition upTo) => _source.Track(upTo);

    /// <summary>Marks every record in the batch as durably handled.</summary>
    public void Track() => _source.Track();

    /// <summary>Requests a commit barrier after this batch. At most one per handler invocation.</summary>
    public void Checkpoint(string? reason = null) => _source.Checkpoint(reason);

    /// <summary>
    /// Requests that processing stop after this batch. The batch is not auto-tracked — claim it with
    /// <see cref="Track()"/> first if the writes should survive.
    /// </summary>
    public void StopProcessing(string? reason = null) => _source.StopProcessing(reason);

    internal static BulkProjectionContext<TClient> From(TClient client, BatchContext source) =>
        new(client, source);
}
