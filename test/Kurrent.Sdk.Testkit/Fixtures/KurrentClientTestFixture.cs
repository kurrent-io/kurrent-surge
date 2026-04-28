using System.Runtime.CompilerServices;
using Kurrent.Client;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;
using Testing.ArcadeRacer;

namespace Kurrent.Sdk.Testkit.Fixtures;

public sealed class KurrentClientTestFixture : KurrentDBTestFixture {
    [ClassDataSource<ArcadeRacerTestDataSource>(Shared = SharedType.PerTestSession)]
    public required ITestDataSource TestData { get; [UsedImplicitly] init; }

    public MessageTypeMapper TypeMapper => Client.Options.Schema.Mapper;

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
