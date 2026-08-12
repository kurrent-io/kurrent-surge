using Kurrent.Surge.Client.Admin;
using Kurrent.Surge.Client.Indexes;
using Kurrent.Surge.Client.Connectors;
using Kurrent.Surge.Client.Legacy;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Governance;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Schema.Serialization.Json;
using Kurrent.Surge.Client.Schema.Serialization.Protobuf;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using ProtobufMessages = Kurrent.Surge.Client.Schema.Serialization.Protobuf.ProtobufMessages;

namespace Kurrent.Surge.Client;

[PublicAPI]
public class KurrentClient : IKurrentClient {
    public KurrentClient(KurrentClientOptions options) {
        options.EnsureValid();

        Options = options;
        
        // Soon enough, the schema registry policy will be retrieved from the server during client
        // initialization (defaults), and then on a per-stream basis during stream operations.
        // For now, we can use a default policy that allows for all operations without enforcement.
        RegistryPolicy = SchemaRegistryPolicy.NoRequirements;
        
        // The freeze: snapshot the message catalogs (system + default + explicit + auto-
        // discovered) into a read-only resolver. Catalog changes after this point never
        // affect this client.
        var (messageResolver, catalogs) = options.Messages.BuildResolver();

        Catalogs = catalogs;

        if (options.Messages.Protobuf.Context is not null)
            ProtobufMessages.System.Configure(options.Messages.Protobuf.Context);

        Registrations = BuildRegistrations(options.Messages);

        // The interceptor is constructed before anything it observes: its closure defers
        // governance resolution to the first gRPC call, so the construction cycle
        // (invoker → registry → governance) never binds here.
        var catalogValidation = new CatalogValidationInterceptor(() =>
            Governance.ValidateCatalog(
                Catalogs,
                RegistryPolicy.Resolve(
                    options: Registrations[SchemaDataFormat.Json], 
                    stream: null, 
                    dataFormat: SchemaDataFormat.Json),
                Lifetime.Token));

        LegacyCallInvoker     = new(options, [catalogValidation]);
        BackdoorClientFactory = new(LegacyCallInvoker, options);
        
        var codecs = new SchemaCodecProvider([
            new JsonSchemaCodec(options.Messages.Json),
            new ProtobufSchemaCodec(),
            ..options.Messages.Codecs
        ]);

        // Modules capture the invoker at construction — registry and governance must
        // follow LegacyCallInvoker, or every client ctor throws on a null invoker.
        Registry   = new RegistryClient(this);
        Governance = SchemaGovernance.Create(this);

        WritePipeline = WriteRecordPipeline.Create(messageResolver, RegistryPolicy, Registrations, codecs, catalogValidation);
        ReadPipeline  = ReadRecordPipeline.Create(messageResolver, RegistryPolicy, Registrations, codecs, Governance);

        Streams    = new StreamsClient(this);
        Indexes    = new IndexesClient(this);
        Connectors = new ConnectorsClient(this);
        Admin      = new AdminClient(this);
    }

    internal bool Shared { get; init; }

    internal KurrentClientOptions         Options               { get; }
    internal KurrentDBLegacyCallInvoker   LegacyCallInvoker     { get; }
    internal KurrentBackdoorClientFactory BackdoorClientFactory { get; }

    internal SchemaRegistryPolicy RegistryPolicy { get; }
    internal WriteRecordPipeline  WritePipeline  { get; }
    internal ReadRecordPipeline   ReadPipeline   { get; }

    IReadOnlyDictionary<SchemaDataFormat, SchemaRegistrationOptions> Registrations { get; }

    ISchemaGovernance             Governance { get; }
    IReadOnlyList<MessageCatalog> Catalogs   { get; }
    CancellationTokenSource       Lifetime   { get; } = new();

    public IStreamsClient    Streams    { get; }
    public IRegistryClient   Registry   { get; }
    public IIndexesClient    Indexes    { get; }
    public IConnectorsClient Connectors { get; }
    public IAdminClient      Admin      { get; }

    public async ValueTask DisposeAsync() {
        if (Shared) return;

        // A disposed client must not leave a catalog validation walk mid-flight.
        await Lifetime.CancelAsync().ConfigureAwait(false);
        Lifetime.Dispose();

        BackdoorClientFactory.Dispose();

        await LegacyCallInvoker
            .DisposeAsync()
            .ConfigureAwait(false);
    }

    static IReadOnlyDictionary<SchemaDataFormat, SchemaRegistrationOptions> BuildRegistrations(KurrentClientMessageOptions messages) {
        var registrations = Enum.GetValues<SchemaDataFormat>().ToDictionary(format => format, _ => new SchemaRegistrationOptions());

        registrations[SchemaDataFormat.Json]     = messages.Json.SchemaRegistration;
        registrations[SchemaDataFormat.Protobuf] = messages.Protobuf.SchemaRegistration;

        return registrations;
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
