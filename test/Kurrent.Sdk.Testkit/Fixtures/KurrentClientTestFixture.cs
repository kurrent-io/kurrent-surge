using System.Runtime.CompilerServices;
using Bogus;
using Kurrent.Client;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams;
using Kurrent.Sdk.Testkit.Hosting;
using Kurrent.Testing.Testkit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Testing.ArcadeRacer;
using TUnit.Core.Interfaces;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Kurrent.Sdk.Testkit.Fixtures;

public sealed class KurrentClientTestFixture : IAsyncInitializer, IAsyncDisposable {
    KurrentDBTestCluster? _cluster;

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [ClassDataSource<ArcadeRacerTestDataSource>(Shared = SharedType.PerTestSession)]
    public required ITestDataSource TestData { get; [UsedImplicitly] init; }

    public ILogger Logger => TestkitExecutionContext.Logger;

    public ILoggerFactory LoggerFactory => TestkitExecutionContext.LoggerFactory;

    public FakeTimeProvider Time { get; } = new();

    public KurrentClient Client => LazyClient.Value;

    public MessageTypeMapper TypeMapper => Client.Options.Schema.Mapper;

    public string ConnectionString { get; private set; } = string.Empty;

    Lazy<KurrentClient> LazyClient { get; set; } = null!;

    public async Task InitializeAsync() {
        _cluster = await new KurrentDBTestClusterBuilder()
            .AddNode()
            .BuildAsync()
            .ConfigureAwait(false);

        ConnectionString = _cluster.AuthenticatedConnectionString;

        LazyClient = new Lazy<KurrentClient>(() =>
            KurrentClientOptions.Parse(ConnectionString).GetBuilder()
                .WithLoggerFactory(TestkitExecutionContext.LoggerFactory)
                .ConfigureSchema(schema => schema
                    .WithValidation(false)
                    .ConfigureMessages(msg => msg.Map<StreamMetadata>("$metadata")))
                .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
                .CreateClient()
        );
    }

    public async ValueTask DisposeAsync() {
        if (LazyClient is { IsValueCreated: true }) {
            try {
                await LazyClient.Value.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Failed to dispose of Client");
                throw;
            }
        }

        if (_cluster is { } cluster)
            await cluster.DisposeAsync();
    }

    public KurrentClient GetNewClient() =>
        KurrentClientOptions.Parse(ConnectionString).GetBuilder()
            .WithLoggerFactory(TestkitExecutionContext.LoggerFactory)
            .ConfigureSchema(schema => schema
                .WithValidation(false)
                .ConfigureMessages(msg => msg.Map<StreamMetadata>("$metadata")))
            .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
            .CreateClient();

    public async ValueTask<RecordPosition> LatestRecordPosition(CancellationToken ct = default) {
        var request = new ReadRecordsRequest {
            Direction    = ReadDirection.Backwards,
            Start        = LogPosition.Latest,
            Limit        = 1,
            SkipDecoding = true
        };

        return await Client.Streams
            .ReadRecords(request, ct)
            .Select(x => x.AsRecord.Position)
            .FirstAsync(ct)
            .ConfigureAwait(false);
    }

    public async ValueTask<SeededTestStream> SeedTestStream(
        int? numberOfEvents = null,
        CancellationToken ct = default
    ) {
        var ds = numberOfEvents is null
            ? await TestData.GenerateSet(ct).ConfigureAwait(false)
            : await TestData.GenerateSet(numberOfEvents.Value, ct).ConfigureAwait(false);

        var req = AppendStreamRequest.For(ds.Stream).WithRecords(ds.Records).Build();
        var rsp = await Client.Streams.AppendStream(req, ct).ConfigureAwait(false);

        var seeded = new SeededTestStream(ds, rsp.Revision, rsp.Position);

        Logger.LogDebug(
            "Seeded {Count} {DataSource} events: {Stream} v{Revision} @ {Position}",
            seeded.Records.Length, TestData.Name, seeded.Stream, seeded.Revision.Value, seeded.Position.Value);

        return seeded;
    }

    public ValueTask<SeededTestStream> SeedTestStream(CancellationToken ct) => SeedTestStream(null, ct);

    public async IAsyncEnumerable<SeededTestStream> TestStreams([EnumeratorCancellation] CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            SeededTestStream seededStream;
            try {
                seededStream = await SeedTestStream(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                yield break;
            }
            yield return seededStream;
        }
    }

    public ValueTask<List<SeededTestStream>> SeedManyTestStreams(int numberOfStreams, CancellationToken ct) =>
        TestStreams(ct).Take(numberOfStreams).ToListAsync(ct);
}
