using System.Diagnostics.CodeAnalysis;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors;

/// <summary>
/// Context provided to a projection handler. Extends <see cref="ProcessRecordContext"/> with
/// a <see cref="GetClient"/> delegate the handler can invoke to resolve the projection's client.
/// </summary>
[PublicAPI]
public record ProjectionContext<TClient>(GetProjectionClient<TClient> GetClient) : ProcessRecordContext {
    [SetsRequiredMembers]
    public ProjectionContext(ProcessRecordContext source, GetProjectionClient<TClient> getClient) : this(getClient) {
        Record            = source.Record;
        ProgressSource    = source.ProgressSource;
        RequestTrack      = source.RequestTrack;
        ProcessorId       = source.ProcessorId;
        DroneId           = source.DroneId;
        SessionId         = source.SessionId;
        Logger            = source.Logger;
        CancellationToken = source.CancellationToken;
    }

    public object Message => Record.Value!;
}

// public record NewProjectionContext<TClient> {
//     public ProcessorId                  ProcessorId       { get; init; } = ProcessorId.Unset;
//     public string                       DroneId           { get; init; } = string.Empty;
//     public SessionId                    SessionId         { get; init; } = SessionId.Unset;
//     public ILogger                      Logger            { get; init; } = NullLogger.Instance;
//     public CancellationToken            CancellationToken { get; init; } = CancellationToken.None;
//     public Record[]                     Records           { get; init; } = [];
//     public DroneProgress                Progress          { get; init; } = new();
//     public GetProjectionClient<TClient> GetClient         { get; init; } = _ => throw new InvalidOperationException("Client not set");
// }
