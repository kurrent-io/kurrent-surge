using Kurrent.Client.Admin;
using Kurrent.Client.Connectors;
using Kurrent.Client.Indexes;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;

namespace Kurrent.Client;

public interface IKurrentClient : IAsyncDisposable {
    IStreamsClient    Streams    { get; }
    IRegistryClient   Registry   { get; }
    IIndexesClient    Indexes    { get; }
    IConnectorsClient Connectors { get; }
    IAdminClient      Admin      { get; }
}
