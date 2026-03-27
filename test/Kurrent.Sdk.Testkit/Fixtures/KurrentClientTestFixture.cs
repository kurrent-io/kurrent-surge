// ReSharper disable ArrangeTypeMemberModifiers

using Bogus;
using Kurrent.Client.Streams;
using Kurrent.Testing.Containers.KurrentDB;
using Kurrent.Testing.Testkit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Testing.ArcadeRacer;
using TUnit.Core.Interfaces;

namespace Kurrent.Client.Tests.Fixtures;

public sealed class KurrentClientTestFixture : IAsyncInitializer, IAsyncDisposable {
    [ClassDataSource<KurrentDBTestContainer>(Shared = SharedType.PerTestSession)]
    public required KurrentDBTestContainer KurrentDB { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [ClassDataSource<ArcadeRacerTestDataSource>(Shared = SharedType.PerTestSession)]
    public required ITestDataSource TestData { get; [UsedImplicitly] init; }

    /// <summary>
    /// The logger used for logging in tests.
    /// </summary>
    public ILogger Logger => TestkitExecutionContext.Logger;

    /// <summary>
    /// The logger factory used for creating loggers in tests.
    /// </summary>
    public ILoggerFactory LoggerFactory => TestkitExecutionContext.LoggerFactory;

    /// <summary>
    /// Time provider used for simulating and controlling time in tests.
    /// </summary>
    public FakeTimeProvider Time { get; } = new FakeTimeProvider();

    /// <summary>
    /// The Kurrent client used for testing.
    /// </summary>
    public KurrentClient Client => LazyClient.Value;

    Lazy<KurrentClient> LazyClient { get; set; } = null!;

    public Task InitializeAsync() {
        LazyClient = new Lazy<KurrentClient>(() => {
            var builder = KurrentClientOptions.Parse(KurrentDB.AuthenticatedConnectionString).GetBuilder()
                .WithLoggerFactory(TestkitExecutionContext.LoggerFactory)
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
            try {
                await LazyClient.Value.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Failed to dispose of Client");
                throw;
            }
        }
    }


    public async ValueTask<SeededTestStream> SeedData(int numberOfEvents, CancellationToken cancellationToken) {
        var ds = await TestData.GenerateSet(numberOfEvents, cancellationToken);

        var rsp = await Client.Streams
            .AppendToStream(AppendToStreamRequest.For(ds.Stream)
                .WithRecords(ds.Records)
                .Build(), cancellationToken);

        var seeded = new SeededTestStream(ds, rsp.Revision, rsp.Position);

        Logger.LogDebug(
            "Seeded {Count} {DataSource} events: {Stream} v{Revision} @ {Position}",
            seeded.Records.Count, TestData.Name, seeded.Stream, seeded.Revision, seeded.Position);

        return seeded;
    }

    public async ValueTask<SeededTestStream> SeedData(CancellationToken cancellationToken) =>
        await SeedData(Random.Shared.Next(15, 60), cancellationToken);
}
