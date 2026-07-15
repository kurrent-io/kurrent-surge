using Kurrent.Surge.Client.Admin;
using Kurrent.Surge.Client.Connectors;
using Kurrent.Surge.Client.Indexes;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Client;

public interface IKurrentClient : IAsyncDisposable {
    IStreamsClient    Streams    { get; }
    IRegistryClient   Registry   { get; }
    IIndexesClient    Indexes    { get; }
    IConnectorsClient Connectors { get; }
    IAdminClient      Admin      { get; }
}
