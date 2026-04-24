using Bogus;
using Kurrent.Client;
using Kurrent.Client.Streams;
using Kurrent.Sdk.Testkit.Hosting;
using Kurrent.Testing.Testkit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TUnit.Core.Interfaces;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Kurrent.Sdk.Testkit.Fixtures;

public class KurrentDBTestFixture : IAsyncInitializer, IAsyncDisposable {
    KurrentDBTestCluster? _cluster;

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    public ILogger Logger => TestkitExecutionContext.Logger;

    public ILoggerFactory LoggerFactory => TestkitExecutionContext.LoggerFactory;

    public FakeTimeProvider Time { get; } = new();

    public KurrentClient Client { get; private set; } = null!;

    public string ConnectionString { get; private set; } = string.Empty;

    public string AnonymousConnectionString { get; private set; } = string.Empty;

    public string? CertsDirectory { get; private set; }

    protected virtual KurrentDBTopology Topology => KurrentDBTopology.Insecure;

    public async Task InitializeAsync() {
        _cluster = await KurrentDBTestCluster.StartAsync(Topology).ConfigureAwait(false);

        ConnectionString          = _cluster.AuthenticatedConnectionString;
        AnonymousConnectionString = _cluster.AnonymousConnectionString;
        CertsDirectory            = _cluster.CertsDirectory;

        Client = KurrentClientOptions.Parse(ConnectionString).GetBuilder()
            .WithLoggerFactory(LoggerFactory)
            .ConfigureSchema(schema => schema
                .WithValidation(false)
                .ConfigureMessages(msg => msg.Map<StreamMetadata>("$metadata")))
            .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
            .CreateClient();
    }

    public async ValueTask DisposeAsync() {
        if (Client is { } client)
            await client.DisposeAsync();

        if (_cluster is { } cluster)
            await cluster.DisposeAsync();
    }
}

public sealed class KurrentDBSecureTestFixture : KurrentDBTestFixture {
    protected override KurrentDBTopology Topology => KurrentDBTopology.Secure;
}

public sealed class KurrentDBClusterTestFixture : KurrentDBTestFixture {
    protected override KurrentDBTopology Topology => KurrentDBTopology.Cluster;
}
