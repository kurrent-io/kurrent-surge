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

    protected virtual int NodeCount => 1;

    protected abstract void ConfigureNode(KurrentDBNodeOptions opts);

    public async Task InitializeAsync() {
        var builder = new KurrentDBTestClusterBuilder();
        for (var i = 0; i < NodeCount; i++)
            builder.AddNode(ConfigureNode);

        _cluster = await builder.BuildAsync().ConfigureAwait(false);

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

public sealed class KurrentDBSecureTestFixture : KurrentDBTestFixture {
    protected override void ConfigureNode(KurrentDBNodeOptions opts) => opts.Secure = true;
}

public sealed class KurrentDBInsecureTestFixture : KurrentDBTestFixture {
    protected override void ConfigureNode(KurrentDBNodeOptions opts) { }
}

public sealed class KurrentDBSecureClusterTestFixture : KurrentDBTestFixture {
    protected override int NodeCount => 3;
    protected override void ConfigureNode(KurrentDBNodeOptions opts) => opts.Secure = true;
}
