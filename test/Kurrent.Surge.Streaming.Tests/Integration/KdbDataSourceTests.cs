using System.Threading.Channels;
using Kurrent.Surge.Client.Streams;
using Kurrent.Streaming.Gateway;
using Polly;

namespace Kurrent.Streaming.Tests;

// Pruning the caught-up prefix moved off the drone and onto the data source (see IDataSource / KdbDataSource).
// These exercise the REAL source against a live KurrentDB: given per-partition floors, the source must feed only
// records past them; given no floor, it must feed everything. This is the home of the prune contract the old
// DroneLifecycleTests.caught_up_records_pruned_before_dispatch unit test used to assert against a fake.
[Category("Processors")]
[Category("Integration")]
[Timeout(120_000)]
public class KdbDataSourceTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask caught_up_records_at_or_below_the_floor_are_not_delivered(CancellationToken ct) {
        // Arrange — one stream, two appends. The first append is the caught-up prefix; its last position is the
        // floor. The second append is the survivors — every one lands past the floor. A single owned partition
        // (MaxParallelism 1) routes every record to partition 0, so the floor is keyed to partition 0.
        var prefix    = await Fixture.TestData.GenerateSet(3, ct);
        var stream    = prefix.Stream;
        var prefixRsp = await Fixture.Client.Streams.AppendStream(stream, prefix.Records, ct);

        var survivors    = await Fixture.TestData.GenerateSet(2, ct);
        var survivorsRsp = await Fixture.Client.Streams.AppendStream(stream, survivors.Records, ct);

        var floor            = RecordPosition.From(prefixRsp.Position.LogPosition, Partition.From(0));
        var expectedCount    = survivors.Records.Length;
        var lastSurvivorPos  = survivorsRsp.Position.LogPosition;

        var source = new KdbDataSource(
            new KdbDataSourceOptions {
                Filter             = RecordFilter.ByStream(stream),
                ResiliencePipeline = ResiliencePipeline.Empty
            },
            Fixture.Client.Streams,
            Fixture.LoggerFactory);

        // Act — feed with the floor; the source subscribes past it and prunes anything at or below it.
        var delivered = await Drain(source, [floor], expectedCount, ct);

        // Assert — exactly the survivors reached the channel, all strictly past the floor, ending at the last append.
        await Assert.That(delivered.Count).IsEqualTo(expectedCount);
        await Assert.That(delivered.All(p => p.LogPosition > floor.LogPosition)).IsTrue();
        await Assert.That(delivered[^1].LogPosition).IsEqualTo(lastSurvivorPos);
    }

    [Test]
    public async ValueTask no_floor_delivers_every_record(CancellationToken ct) {
        // Arrange — with no real floor the prune set is empty (the same state a Latest/Unset floor collapses to,
        // since those sentinels are excluded), so nothing is caught up and every record must flow through.
        var data          = await Fixture.TestData.GenerateSet(4, ct);
        var rsp           = await Fixture.Client.Streams.AppendStream(data.Stream, data.Records, ct);
        var expectedCount = data.Records.Length;
        var lastPos       = rsp.Position.LogPosition;

        var source = new KdbDataSource(
            new KdbDataSourceOptions {
                Filter             = RecordFilter.ByStream(data.Stream),
                ResiliencePipeline = ResiliencePipeline.Empty
            },
            Fixture.Client.Streams,
            Fixture.LoggerFactory);

        // Act — no floor: subscribe from the earliest position, prune nothing.
        var delivered = await Drain(source, [], expectedCount, ct);

        // Assert — every appended record was delivered, in order, ending at the last append.
        await Assert.That(delivered.Count).IsEqualTo(expectedCount);
        await Assert.That(delivered[^1].LogPosition).IsEqualTo(lastPos);
    }

    // Drives the source over a fresh channel and collects the record positions it feeds. The subscription is
    // live and never completes on its own, so stop as soon as the run has caught up (server CaughtUpCheckpoint)
    // or as many records as expected have arrived — whichever first — then cancel and let Connect drain out.
    static async Task<List<RecordPosition>> Drain(
        KdbDataSource source, RecordPosition[] floors, int expectedCount, CancellationToken ct
    ) {
        var channel = Channel.CreateUnbounded<DataSourceMessage>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var connect = source.Connect(channel.Writer, floors, cts.Token);

        var delivered = new List<RecordPosition>();
        var caughtUp  = false;

        await foreach (var msg in channel.Reader.ReadAllAsync(ct)) {
            switch (msg.Value) {
                case Record rec:         delivered.Add(rec.Position); break;
                case CaughtUpCheckpoint: caughtUp = true;             break;
            }

            if (caughtUp || delivered.Count >= expectedCount)
                break;
        }

        await cts.CancelAsync();

        try {
            await connect;
        }
        catch (OperationCanceledException) {
            // expected: cancelling the run tears the live subscription down.
        }

        return delivered;
    }
}
