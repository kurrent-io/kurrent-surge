// ReSharper disable ArrangeTypeMemberModifiers

using System.Runtime.CompilerServices;
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

    public async ValueTask<LogPosition> LatestLogPosition(CancellationToken ct = default) {
        var options = new ReadOptions() {
            Direction    = ReadDirection.Backwards,
            Start        = LogPosition.Latest,
            MaxRecords   = 1,
            SkipDecoding = true,
            CancellationToken = ct
        };

        await using var reader = await Client.Streams.ReadRecords(options);

        return await reader.Take(1)
            .Select(x => x.AsRecord.Position.LogPosition)
            .SingleAsync(ct);
    }

    public async ValueTask<SeededTestStream> SeedTestStream(
        int? numberOfEvents = null,
        CancellationToken ct = default
    ) {
        var ds = numberOfEvents is null
            ? await TestData.GenerateSet(ct)
            : await TestData.GenerateSet(numberOfEvents.Value, ct);

        var req = AppendToStreamRequest.For(ds.Stream).WithRecords(ds.Records).Build();

        var rsp = await Client.Streams.AppendToStream(req, ct);

        var seeded = new SeededTestStream(ds, rsp.Revision, rsp.Position);

        Logger.LogDebug(
            "Seeded {Count} {DataSource} events: {Stream} v{Revision} @ {Position}",
            seeded.Records.Count, TestData.Name, seeded.Stream, seeded.Revision, seeded.Position);

        return seeded;
    }

    public ValueTask<SeededTestStream> SeedTestStream(CancellationToken ct) => SeedTestStream(null, ct);

    public async IAsyncEnumerable<SeededTestStream> TestStreams([EnumeratorCancellation] CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            SeededTestStream? seededStream = null;

            try {
                seededStream = await SeedTestStream(ct);
            }
            catch (OperationCanceledException) {
                yield break;
            }

            if (seededStream is not null)
                yield return seededStream;
        }
    }

    public ValueTask<List<SeededTestStream>> SeedManyTestStreams(int numberOfStreams, CancellationToken ct) =>
        TestStreams(ct).Take(numberOfStreams).ToListAsync(ct);
}
