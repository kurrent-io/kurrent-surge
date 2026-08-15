using Grpc.Core;
using Kurrent.Surge.Client.Admin;
using Kurrent.Surge.Client.Indexes;
using Kurrent.Surge.Client.Connectors;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Governance;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Schema.Serialization.Json;
using Kurrent.Surge.Client.Schema.Serialization.Protobuf;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Client.LoadBalancing;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using ProtobufMessages = Kurrent.Surge.Client.Schema.Serialization.Protobuf.ProtobufMessages;

namespace Kurrent.Surge.Client;

[PublicAPI]
public class KurrentClient : IKurrentClient {
    public KurrentClient(KurrentClientOptions options) {
	    options.EnsureValid(options.LoggerFactory.CreateLogger<KurrentClient>());

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

        // The gate tests the RESOLVED policy, never the raw dial: when server policy
        // resolution goes live, a server-side Required overrides even a lax client dial,
        // and this gate keeps working because resolution already carries the server's voice.
        var resolvedPolicy = RegistryPolicy.Resolve(options.Schema, stream: null, SchemaDataFormat.Json);

        // Never throw-on-failure: the walk's failure travels its own channel — the result
        // task the govern steps await — so it surfaces at the first governed operation
        // without bricking reads of raw or unmapped records. The task captures the concrete
        // client because governance does not exist yet; it resolves at execution time.
        InitializationTaskRegistration[] builtins = resolvedPolicy.SchemaRegistryRequired
            ? [new(new CatalogRegistrationTask(this), ThrowOnFailure: false)]
            : [];

        Initializer = new(
            [..builtins, ..options.Initialization.Tasks],
            options.LoggerFactory.CreateLogger<ClientInitializer>());

        // The fault gate rides the invokers, never the channel config: every module —
        // current and future — is constructed over these references, so a faulted client
        // makes no calls without any operation naming the gate.
        Channels        = ClusterChannels.Create(options);
        CallInvoker     = new FaultGateCallInvoker(Channels.Write.CallInvoker, Initializer);
        ReadCallInvoker = new FaultGateCallInvoker(Channels.Read.CallInvoker, Initializer);

        BackdoorClientFactory = new(Channels.Write, options, Initializer);

        var codecs = new SchemaCodecProvider([
            new JsonSchemaCodec(options.Messages.Json),
            new ProtobufSchemaCodec(),
            ..options.Messages.Codecs
        ]);

        // Modules capture the invoker at construction — registry and governance must
        // follow the channel wiring, or every client ctor throws on a null invoker.
        Registry   = new RegistryClient(this);
        Governance = SchemaGovernance.Create(this, Catalogs, resolvedPolicy, Lifetime.Token);

        WritePipeline = WriteRecordPipeline.Create(messageResolver, RegistryPolicy, options.Schema, codecs, Governance);
        ReadPipeline  = ReadRecordPipeline.Create(messageResolver, RegistryPolicy, options.Schema, codecs, Governance);

        Streams    = new StreamsClient(this);
        Indexes    = new IndexesClient(this);
        Connectors = new ConnectorsClient(this);
        Admin      = new AdminClient(this);

        // Initialization runs off-thread once the first connection is established. The
        // watcher is passive — a client that never makes a call never initializes.
        _ = RunInitialization();

        async Task RunInitialization() {
            if (await Channels.WaitForConnectedAsync(Lifetime.Token).ConfigureAwait(false))
                await Initializer.Run(this, Lifetime.Token).ConfigureAwait(false);
        }
    }

    internal bool Shared { get; init; }

    internal KurrentClientOptions         Options               { get; }
    internal ClusterChannels              Channels              { get; }
    internal CallInvoker                  CallInvoker           { get; }
    internal CallInvoker                  ReadCallInvoker       { get; }
    internal KurrentBackdoorClientFactory BackdoorClientFactory { get; }

    internal SchemaRegistryPolicy RegistryPolicy { get; }
    internal ClientInitializer    Initializer    { get; }
    internal WriteRecordPipeline  WritePipeline  { get; }
    internal ReadRecordPipeline   ReadPipeline   { get; }

    internal ISchemaGovernance Governance { get; }

    IReadOnlyList<MessageCatalog> Catalogs   { get; }
    CancellationTokenSource       Lifetime   { get; } = new();

    public IStreamsClient    Streams    { get; }
    public IRegistryClient   Registry   { get; }
    public IIndexesClient    Indexes    { get; }
    public IConnectorsClient Connectors { get; }
    public IAdminClient      Admin      { get; }

    /// <summary>
    /// Connects ahead of the first operation: runs discovery, establishes the connections,
    /// and lets the initialization tasks fire. Optional — without it the client connects
    /// on the first operation.
    /// </summary>
    public ValueTask ConnectAsync(CancellationToken cancellationToken = default) =>
        Channels.ConnectAsync(cancellationToken);

    public async ValueTask DisposeAsync() {
        if (Shared) return;

        // A disposed client must not leave a catalog validation walk mid-flight.
        await Lifetime.CancelAsync().ConfigureAwait(false);
        Lifetime.Dispose();

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
