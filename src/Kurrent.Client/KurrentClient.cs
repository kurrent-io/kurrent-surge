using Kurrent.Client.Connectors;
using Kurrent.Client.Legacy;
using Kurrent.Client.Registry;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Schema.Serialization.Bytes;
using Kurrent.Client.Schema.Serialization.Json;
using Kurrent.Client.Schema.Serialization.Protobuf;
using Kurrent.Client.Streams;

namespace Kurrent.Client;

[PublicAPI]
public class KurrentClient : IAsyncDisposable {
    public static KurrentClientOptionsBuilder New => new KurrentClientOptionsBuilder();

    public KurrentClient(KurrentClientOptions options) {
	    options.EnsureOptionsAreValid();

	    Options = options;

        LegacyCallInvoker     = new KurrentDBLegacyCallInvoker(options);
        BackdoorClientFactory = new KurrentBackdoorClientFactory(LegacyCallInvoker, options);

        var schemaManager = new SchemaManager(
	        new RegistryClient(this),
	        NJsonSchemaExporter.Instance,
	        options.Mapper);

        SerializerProvider = new SchemaSerializerProvider([
	        new BytesPassthroughSerializer(),
	        new JsonSchemaSerializer(new() { SchemaRegistration = SchemaRegistrationOptions.AutoMap }, schemaManager),
	        new ProtobufSchemaSerializer(new() { SchemaRegistration = SchemaRegistrationOptions.AutoMap }, schemaManager)
        ]);

        Registry                = new RegistryClient(this);
        Streams                 = new StreamsClient(this);
        Connectors              = new ConnectorsClient(this);
        // Admin                   = new AdminClient(this);
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
    public ConnectorsClient              Connectors              { get; }
    // public UsersClient                   Users                   { get; }
    // public PersistentSubscriptionsClient PersistentSubscriptions { get; }
    // public AdminClient                   Admin                   { get; }
    // public ProjectionsClient             Projections             { get; }

	// internal async Task<ServerFeatures> ForceRefresh(CancellationToken cancellationToken = default) {
	// 	await LegacyCallInvoker.ForceRefresh(cancellationToken).ConfigureAwait(false);
	//
	// 	return new ServerFeatures {
	// 		Version = LegacyCallInvoker.ServerCapabilities.Version
	// 	};
	// }

	public virtual async ValueTask DisposeAsync() {
        if (Shared) return;

        BackdoorClientFactory.Dispose();

        await LegacyCallInvoker
            .DisposeAsync()
            .ConfigureAwait(false);
    }

    public static KurrentClient Create(KurrentClientOptions? options = null) =>
        new(options ?? new KurrentClientOptions());

    public static KurrentClient Create(string connectionString) =>
        Create(KurrentConnectionString.Parse(connectionString).ToClientOptions());
}
