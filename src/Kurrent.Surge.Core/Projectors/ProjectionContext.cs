using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors;

/// <summary>
/// Context for a handler that projects one record.
/// </summary>
/// <remarks>
/// <see cref="Client"/> is the client of the transaction the projection currently has open — the
/// framework resolves it, never the handler. A handler that obtained its own connection would write
/// outside that transaction, so a rollback would leave those writes behind and the checkpoint would
/// commit as if they had happened.
/// </remarks>
[PublicAPI]
public readonly record struct ProjectionContext<TClient> {
    public required TClient           Client            { get; init; }
    public required Record            Record            { get; init; }
    public required string            ProjectionId      { get; init; }
    public required ILogger           Logger            { get; init; }
    public required CancellationToken CancellationToken { get; init; }

    internal static ProjectionContext<TClient> From(TClient client, Record record, BatchContext source) =>
        new() {
            Client            = client,
            Record            = record,
            ProjectionId      = source.ProcessorId,
            Logger            = source.Logger,
            CancellationToken = source.CancellationToken
        };
}

