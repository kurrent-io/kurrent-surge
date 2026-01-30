// ReSharper disable ArrangeTypeMemberModifiers

using Kurrent.Client.Streams;
using Kurrent.Testing.Bogus;
using Kurrent.Testing.Containers.KurrentDB;
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
    public ILogger Logger => TestContext.CurrrentLogger;

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
    public KurrentClient Client { get; private set; } = null!;

    public Task InitializeAsync() {
       var lazyClient = new Lazy<KurrentClient>(() => {
            var builder = KurrentClientOptions.Parse(KurrentDB.AuthenticatedConnectionString).GetBuilder()
                .WithLoggerFactory(new TestkitLoggerFactory())
                .ConfigureSchema(KurrentClientSchemaOptions.NoValidation)
                .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
                .ConfigureMessages(options => options.Map<StreamMetadata>("$metadata"));

            return builder.CreateClient();
        });

        Client = lazyClient.Value;

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() =>
        await Client.DisposeAsync().ConfigureAwait(false);
}
