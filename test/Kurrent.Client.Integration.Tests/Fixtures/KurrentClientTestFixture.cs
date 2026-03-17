// ReSharper disable ArrangeTypeMemberModifiers

using Kurrent.Client.Streams;
using Kurrent.Testing.Bogus;
using Kurrent.Testing.Containers.KurrentDB;
using Kurrent.Testing.Testkit;
using Kurrent.Testing.TUnit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TUnit.Core.Interfaces;

using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Kurrent.Client.Tests.Fixtures;

public sealed partial class KurrentClientTestFixture : IAsyncInitializer, IAsyncDisposable {
    [ClassDataSource<KurrentDBTestContainer>(Shared = SharedType.PerTestSession)]
    public required KurrentDBTestContainer KurrentDB { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    /// <summary>
    /// The logger used for logging in tests.
    /// </summary>
    public ILogger Logger => TestContext.CurrentLogger;

    /// <summary>
    /// The logger factory used for creating loggers in tests.
    /// </summary>
    public ILoggerFactory LoggerFactory => TestContext.CurrentLoggerFactory;

    /// <summary>
    /// Time provider used for simulating and controlling time in tests.
    /// </summary>
    public FakeTimeProvider Time { get; } = new FakeTimeProvider();

    /// <summary>
    /// The Kurrent client used for testing.
    /// </summary>
    public KurrentClient Client => LazyClient.Value;

    Lazy<KurrentClient> LazyClient { get; set; }

    public Task InitializeAsync() {
        LazyClient = new Lazy<KurrentClient>(() => {
            var builder = KurrentClientOptions.Parse(KurrentDB.AuthenticatedConnectionString).GetBuilder()
                .WithLoggerFactory(new TestkitLoggerFactory())
                .ConfigureSchema(schema => schema
                    .WithValidation(false)
                    .ConfigureMessages(msg => msg.Map<StreamMetadata>("$metadata")))
                .ConfigureResilience(KurrentClientResilienceOptions.NoResilience);

            return builder.CreateClient();
        });

        if(!KurrentDB.Enabled) {
            Logger.LogWarning(
                "KurrentDB container is disabled. Expecting localhost:2113 to be available. Connection string: {ConnectionString}",
                KurrentDB.AuthenticatedConnectionString);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() {
        if (LazyClient.IsValueCreated) {
            await LazyClient.Value.DisposeAsync().ConfigureAwait(false);
        }
    }
}
