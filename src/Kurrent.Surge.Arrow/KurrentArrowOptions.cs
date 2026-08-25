using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Logging;

namespace Kurrent.Surge.Arrow;

/// <summary>
/// Configures a <see cref="KurrentArrowClient"/>: where it connects, how it authenticates,
/// and how query results are decoded.
/// </summary>
[PublicAPI]
public sealed class KurrentArrowOptions {
    // Every exposed member forwards into these. Schema governance, initialization tasks and the
    // write path are never surfaced — a read-only SQL client has no use for them.
    KurrentClientOptions Client { get; } = new();

    /// <summary>
    /// Creates options from a KurrentDB connection string.
    /// </summary>
    public static KurrentArrowOptions Parse(string connectionString) =>
        new() { ConnectionString = connectionString };

    /// <summary>
    /// The KurrentDB connection string, e.g. <c>kurrentdb://admin:changeit@localhost:2113</c>.
    /// </summary>
    /// <remarks>
    /// Applied in place, so credentials or gossip settings configured before this stays set
    /// unless the connection string names them.
    /// </remarks>
    public string ConnectionString {
        get => Client.OriginalConnectionString;
        set => Client.Connection(value);
    }

    /// <summary>
    /// Message catalogs and codecs. Queried records resolve their type through these, so a
    /// record whose schema is not mapped here comes back raw.
    /// </summary>
    public KurrentClientMessageOptions Messages => Client.Messages;

    /// <summary>
    /// Transport security and authentication credentials.
    /// </summary>
    public KurrentClientSecurityOptions Security {
        get => Client.Security;
        set => Client.Security = value;
    }

    /// <summary>
    /// Gossip discovery settings, including which node answers queries.
    /// </summary>
    public KurrentClientGossipOptions Gossip {
        get => Client.Gossip;
        set => Client.Gossip = value;
    }

    /// <summary>
    /// Retry and hedging strategies.
    /// </summary>
    public KurrentClientResilienceOptions Resilience {
        get => Client.Resilience;
        set => Client.Resilience = value;
    }

    /// <summary>
    /// Wire compression.
    /// </summary>
    public KurrentClientCompressionOptions Compression {
        get => Client.Compression;
        set => Client.Compression = value;
    }

    /// <summary>
    /// Decodes record metadata. Runs for every queried record, including when
    /// <see cref="SkipDecoding"/> is set.
    /// </summary>
    public IMetadataDecoder MetadataDecoder {
        get => Client.MetadataDecoder;
        set => Client.MetadataDecoder = value;
    }

    /// <summary>
    /// Logger factory used by the client and its connection.
    /// </summary>
    public ILoggerFactory LoggerFactory {
        get => Client.LoggerFactory;
        set => Client.LoggerFactory = value;
    }

    /// <summary>
    /// Returns records without materializing their payloads. The decoder still travels with
    /// each record, so <see cref="Record.TryDecode"/> works later — only the eager decode is
    /// skipped.
    /// </summary>
    public bool SkipDecoding { get; set; }

    internal KurrentClientOptions ToClientOptions() => Client;
}
