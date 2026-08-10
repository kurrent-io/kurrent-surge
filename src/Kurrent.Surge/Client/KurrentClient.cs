using Grpc.Core;
using Kurrent.Surge.Client.Admin;
using Kurrent.Surge.Client.Indexes;
using Kurrent.Surge.Client.Connectors;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Experimental.LoadBalancing;
using KurrentDB.Client;
using ProtobufMessages = Kurrent.Surge.Client.Schema.Serialization.Protobuf.ProtobufMessages;

namespace Kurrent.Surge.Client;

[PublicAPI]
public class KurrentClient : IKurrentClient {
    public KurrentClient(KurrentClientOptions options) {
	    options.EnsureValid();

	    Options = options;

        Channels        = ClusterChannels.Create(options);
        CallInvoker     = Channels.Write.CallInvoker;
        ReadCallInvoker = Channels.Read.CallInvoker;

        BackdoorClientFactory = new(Channels.Write, options);

        // The freeze: snapshot the message catalogs (system + default + explicit + auto-
        // discovered) into a read-only resolver. Catalog changes after this point never
        // affect this client.
        var messageResolver = options.Messages
            .UseBytes()
            .UseJson()
            .UseProtobuf()
            .BuildResolver();
        
        if (options.Messages.Protobuf.Context is not null)
            ProtobufMessages.System.Configure(options.Messages.Protobuf.Context);

        var schemaManager = new SchemaManager(
	        new(this), NJsonSchemaExporter.Instance,
	        messageResolver);
        
        // Deferred: every registered factory (built-in or custom) runs now, once schemaManager exists.
        SerializerProvider = new SchemaSerializerProvider(
            options.Messages.SerializerFactories.Select(factory => factory(schemaManager))
        );

        Registry   = new RegistryClient(this);
        Streams    = new StreamsClient(this);
        Indexes    = new IndexesClient(this);
        Connectors = new ConnectorsClient(this);
        Admin      = new AdminClient(this);
    }

    internal bool Shared { get; init; }

    internal KurrentClientOptions         Options               { get; }
    internal ClusterChannels              Channels              { get; }
    internal CallInvoker                  CallInvoker           { get; }
    internal CallInvoker                  ReadCallInvoker       { get; }
    internal KurrentBackdoorClientFactory BackdoorClientFactory { get; }
    internal ISchemaSerializerProvider    SerializerProvider    { get; }

    public IStreamsClient    Streams    { get; }
    public IRegistryClient   Registry   { get; }
    public IIndexesClient    Indexes    { get; }
    public IConnectorsClient Connectors { get; }
    public IAdminClient      Admin      { get; }

    public async ValueTask DisposeAsync() {
        if (Shared) return;

        BackdoorClientFactory.Dispose();

        await Channels
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
    ///     opts.Messages.MapMessage&lt;OrderPlaced&gt;("order.placed.v1");
    /// });
    /// </code>
    /// </example>
    public static KurrentClient Create(string connectionString, Action<KurrentClientOptions>? configure = null) {
        var options = KurrentClientOptions.Parse(connectionString);
        configure?.Invoke(options);
        return Create(options);
    }
}
