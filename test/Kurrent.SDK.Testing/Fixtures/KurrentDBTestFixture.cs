using Kurrent.Client;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams;
using Kurrent.SDK.Testing.Hosting;
using Kurrent.Testing;
using Kurrent.Testing.Bogus;
using Kurrent.Testing.Testkit;
using Kurrent.Testing.TUnit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TUnit.Core.Interfaces;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Kurrent.SDK.Testing.Fixtures;

public sealed class KurrentDBTestFixture : IAsyncInitializer, IAsyncDisposable {
    KurrentDBTestCluster? _cluster;

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    public ILogger Logger => TestContext.CurrentLogger;

    public ILoggerFactory LoggerFactory => TestContext.CurrentLoggerFactory;

    public FakeTimeProvider Time { get; } = new();

    public KurrentClient Client { get; private set; } = null!;

    public string ConnectionString { get; private set; } = string.Empty;

    public string AnonymousConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync() {
        _cluster = await new KurrentDBTestClusterBuilder()
            .AddNode()
            .BuildAsync()
            .ConfigureAwait(false);

        ConnectionString          = _cluster.AuthenticatedConnectionString;
        AnonymousConnectionString = _cluster.AnonymousConnectionString;

        var options = KurrentClientOptions.Parse(ConnectionString);
        options.LoggerFactory          = new TestkitLoggerFactory();
        options.Schema.PreValidate     = false;
        options.Schema.Mapper          = MessageTypeMapper.Create(m => m.Map<StreamMetadata>("$metadata"));
        options.Resilience             = KurrentClientResilienceOptions.NoResilience;
        Client = KurrentClient.Create(options);
    }

    public async ValueTask DisposeAsync() {
        if (Client is { } client)
            await client.DisposeAsync();

        if (_cluster is { }  cluster)
            await cluster.DisposeAsync();
    }
}
