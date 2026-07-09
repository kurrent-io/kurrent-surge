using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Logging;

namespace Kurrent.Surge.Client;

/// <summary>
/// Implementation-agnostic client configuration contract. Holds only the knobs a client
/// needs regardless of transport — an in-process client hosted inside KurrentDB would use
/// exactly these. Connection-flavored configuration (endpoints, gossip, security, resilience)
/// is specific to the remote gRPC implementation and lives with the concrete options there.
/// </summary>
public interface IKurrentClientOptions {
    IMetadataDecoder MetadataDecoder { get; set; }
    ILoggerFactory LoggerFactory { get; set; }
    KurrentClientMessageOptions Messages { get; }
}
