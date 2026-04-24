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

public abstract class KurrentDBTestFixture : IAsyncInitializer, IAsyncDisposable {
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

    protected abstract Task<KurrentDBTestCluster> CreateClusterAsync();

    public async Task InitializeAsync() {
        _cluster = await CreateClusterAsync().ConfigureAwait(false);

        ConnectionString          = _cluster.AuthenticatedConnectionString;
        AnonymousConnectionString = _cluster.AnonymousConnectionString;
        CertsDirectory            = _cluster.CertsDirectory;

        Client = KurrentClientOptions.Parse(ConnectionString).GetBuilder()
            .WithLoggerFactory(TestkitExecutionContext.LoggerFactory)
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

/// <summary>
/// Single-node insecure fixture. Use as the default for tests that exercise client
/// operations (reads, writes, subscriptions) and do not depend on TLS, auth, or
/// cluster semantics. Fastest to spin up.
/// </summary>
public sealed class KurrentDBInsecureTestFixture : KurrentDBTestFixture {
    protected override Task<KurrentDBTestCluster> CreateClusterAsync() =>
        KurrentDBTestCluster.StartAsync();
}

/// <summary>
/// Single-node secure fixture. Use for tests that specifically exercise TLS, user
/// authentication, or certificate handling.
/// </summary>
public sealed class KurrentDBSecureTestFixture : KurrentDBTestFixture {
    protected override Task<KurrentDBTestCluster> CreateClusterAsync() =>
        KurrentDBTestCluster.StartAsync(node => node.WithSecureMode());
}

/// <summary>
/// Three-node secure replica set fixture. Use for tests that depend on cluster
/// semantics such as gossip, leader discovery, node preference, or failover.
/// Slowest to spin up; avoid unless the test actually needs multiple nodes.
/// </summary>
public sealed class KurrentDBClusterTestFixture : KurrentDBTestFixture {
    protected override Task<KurrentDBTestCluster> CreateClusterAsync() =>
        KurrentDBTestCluster.StartAsync(node => node.WithReplicas(2).WithSecureMode());
}
