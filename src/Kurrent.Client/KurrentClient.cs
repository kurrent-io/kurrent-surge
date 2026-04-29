using Kurrent.Client.Admin;
using Kurrent.Client.Indexes;
using Kurrent.Client.Connectors;
using Kurrent.Client.Legacy;
using Kurrent.Client.Registry;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Schema.Serialization.Bytes;
using Kurrent.Client.Schema.Serialization.Json;
using Kurrent.Client.Schema.Serialization.Protobuf;
using Kurrent.Client.Streams;
using ProtobufMessages = Kurrent.Client.Schema.Serialization.Protobuf.ProtobufMessages;

namespace Kurrent.Client;

public interface IKurrentClient : IAsyncDisposable {
    StreamsClient    Streams    { get; }
    RegistryClient   Registry   { get; }
    IndexesClient    Indexes    { get; }
    ConnectorsClient Connectors { get; }
    AdminClient      Admin      { get; }
}

[PublicAPI]
public class KurrentClient : IKurrentClient {
    // public KurrentClient() : this(new()) { }

    public KurrentClient(KurrentClientOptions options) {
	    options.EnsureValid();

	    Options = options;

        LegacyCallInvoker     = new(options);
        BackdoorClientFactory = new(LegacyCallInvoker, options);

        var schemaManager = new SchemaManager(
	        new(this),
	        NJsonSchemaExporter.Instance,
	        options.Schema.Mapper);

        if (options.Schema.Protobuf.Context is not null)
	        ProtobufMessages.System.Configure(options.Schema.Protobuf.Context);

        SerializerProvider = new SchemaSerializerProvider([
	        new BytesPassthroughSerializer(),
	        new JsonSchemaSerializer(options.Schema.Json, schemaManager),
	        new ProtobufSchemaSerializer(options.Schema.Protobuf, schemaManager)
        ]);

        Registry   = new(this);
        Streams    = new(this);
        Indexes    = new(this);
        Connectors = new(this);
        Admin      = new(this);
        // PersistentSubscriptions = new PersistentSubscriptionsClient(this);
        // Projections             = new ProjectionsClient(this);
        // Users                   = new UsersClient(this);
    }

    internal bool Shared { get; init; }

    internal KurrentClientOptions         Options               { get; }
    internal KurrentDBLegacyCallInvoker   LegacyCallInvoker     { get; }
    internal KurrentBackdoorClientFactory BackdoorClientFactory { get; }
    internal ISchemaSerializerProvider    SerializerProvider    { get; }

    public StreamsClient                 Streams                 { get; }
    public RegistryClient                Registry                { get; }
    public IndexesClient                 Indexes                 { get; }
    public ConnectorsClient              Connectors              { get; }
    // public UsersClient                   Users                   { get; }
    // public PersistentSubscriptionsClient PersistentSubscriptions { get; }
    public AdminClient                   Admin                   { get; }
    // public ProjectionsClient             Projections             { get; }

	// internal async Task<ServerFeatures> ForceRefresh(CancellationToken cancellationToken = default) {
	// 	await LegacyCallInvoker.ForceRefresh(cancellationToken).ConfigureAwait(false);
	//
	// 	return new ServerFeatures {
	// 		Version = LegacyCallInvoker.ServerCapabilities.Version
	// 	};
	// }
    
    public async ValueTask DisposeAsync() {
        if (Shared) return;

        BackdoorClientFactory.Dispose();

        await LegacyCallInvoker
            .DisposeAsync()
            .ConfigureAwait(false);
    }
    
    /// <summary>
    /// Creates a client from a pre-built <see cref="KurrentClientOptions"/> instance.
    /// </summary>
    public static KurrentClient Create(KurrentClientOptions options) =>
        new(options);

    /// <summary>
    /// Creates a client from a connection string, with optional further tuning via <paramref name="configure"/>.
    /// The string is parsed into options first; the delegate (if provided) then mutates the parsed options
    /// before the client is constructed.
    /// </summary>
    /// <example>
    /// <code>
    /// // Simple: just the connection string.
    /// var client = KurrentClient.Create("kurrentdb://localhost:2113");
    ///
    /// // Parse the connection string, then tune.
    /// var client = KurrentClient.Create("kurrentdb://localhost:2113", opts => {
    ///     opts.Resilience = KurrentClientResilienceOptions.HighAvailability;
    ///     opts.Schema.Mapper.Map&lt;OrderPlaced&gt;();
    /// });
    /// </code>
    /// </example>
    public static KurrentClient Create(string connectionString, Action<KurrentClientOptions>? configure = null) {
        var options = KurrentClientOptions.Parse(connectionString);
        configure?.Invoke(options);
        return Create(options);
    }
}
