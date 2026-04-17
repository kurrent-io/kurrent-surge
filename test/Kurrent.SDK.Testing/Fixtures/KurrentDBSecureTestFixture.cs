using Bogus;
using Kurrent.Client;
using Kurrent.Client.Streams;
using Kurrent.SDK.Testing.Hosting;
using Kurrent.Testing;
using Kurrent.Testing.Testkit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TUnit.Core.Interfaces;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Kurrent.SDK.Testing.Fixtures;

public sealed class KurrentDBSecureTestFixture : IAsyncInitializer, IAsyncDisposable {
    KurrentDBTestCluster? _cluster;

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    public ILogger Logger => TestkitExecutionContext.Logger;

    public ILoggerFactory LoggerFactory => TestkitExecutionContext.LoggerFactory;

    public FakeTimeProvider Time { get; } = new();

    public KurrentClient Client { get; private set; } = null!;

    public string ConnectionString { get; private set; } = string.Empty;

    public string AnonymousConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync() {
        _cluster = await new KurrentDBTestClusterBuilder()
            .AddNode(opts => {
                opts.Secure = true;
                opts.Image  = KurrentDBContainerImageTags.Nightly.Image;
                opts.Tag    = KurrentDBContainerImageTags.Nightly.Tag;
            })
            .BuildAsync()
            .ConfigureAwait(false);

        ConnectionString          = _cluster.AuthenticatedConnectionString;
        AnonymousConnectionString = _cluster.AnonymousConnectionString;

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
