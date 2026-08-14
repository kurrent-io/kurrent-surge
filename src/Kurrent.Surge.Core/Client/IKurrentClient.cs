using Kurrent.Surge.Client.Admin;
using Kurrent.Surge.Client.Connectors;
using Kurrent.Surge.Client.Indexes;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Client;

public interface IKurrentClient : IAsyncDisposable {
    /// <summary>
    /// Connects ahead of the first operation: runs discovery, establishes the connections,
    /// and lets the initialization tasks fire. Optional — without it the client connects
    /// on the first operation.
    /// </summary>
    ValueTask ConnectAsync(CancellationToken cancellationToken = default);

    IStreamsClient    Streams    { get; }
    IRegistryClient   Registry   { get; }
    IIndexesClient    Indexes    { get; }
    IConnectorsClient Connectors { get; }
    IAdminClient      Admin      { get; }
}
