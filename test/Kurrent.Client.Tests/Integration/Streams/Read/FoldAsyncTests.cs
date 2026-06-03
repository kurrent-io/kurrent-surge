using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Read")]
[Category("Integration")]
[Timeout(30_000)]
public class FoldAsyncTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }
    
    sealed class EvolvedState {
        public List<Guid> RecordIds { get; set; } = [];
    }

    [Test]
    public async ValueTask folds_every_record_in_a_stream(CancellationToken ct) {
        // Arrange
        var data         = await Fixture.SeedTestStream(ct);
        var expectedIds  = data.Records.Select(rec => rec.RecordId).ToList();
        var expectedLast = Revision.From(data.Records.Length - 1);

        // Act — read the stream, then fold the resulting sequence through the extension
        var response = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct)
            .FoldAsync<EvolvedState>((state, record) => {
                state.RecordIds.Add(record.Id);
                return state;
            });

        // Assert
        await Assert.That(response.State.RecordIds.SequenceEqual(expectedIds)).IsTrue()
            .Because("records must fold in stream order");
        await Assert.That(response.Position.Stream).IsEqualTo(data.Stream);
        await Assert.That(response.Position.Revision).IsEqualTo(expectedLast);
    }

    [Test]
    public async ValueTask seeds_the_fold_with_the_provided_initial_state(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(ct);
        var seedId      = Guid.NewGuid();
        var seed        = new EvolvedState { RecordIds = [seedId] };
        var expectedIds = new[] { seedId }.Concat(data.Records.Select(rec => rec.RecordId)).ToList();

        // Act
        var response = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct)
            .FoldAsync<EvolvedState>((state, record) => {
                state.RecordIds.Add(record.Id);
                return state;
            }, seed);

        // Assert
        await Assert.That(response.State.RecordIds.SequenceEqual(expectedIds)).IsTrue();
    }

    [Test]
    public async ValueTask folds_log_records_by_filter(CancellationToken ct) {
        // Arrange
        var start = await Fixture.LatestRecordPosition(ct);
        var data  = await Fixture.SeedTestStream(ct);

        var expectedState = new EvolvedState {
            RecordIds = data.Records.Select(rec => rec.RecordId).ToList()
        };

        var options = new ReadRecordsOptions {
            Start        = start.LogPosition,
            Filter       = RecordFilter.ByExpression($"stream like '{data.Stream.Category}-%'"),
            SkipDecoding = true
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        var response = await Fixture.Client.Streams
            .ReadRecords(options, cts.Token)
            .FoldAsync<EvolvedState>(async (state, record, token) => {
                if (record.Stream == data.Stream)
                    state.RecordIds.Add(record.Id);
                if (state.RecordIds.Count == data.Records.Length)
                    await cts.CancelAsync();
                return state;
            }, cts.Token);

        // Assert
        await Assert.That(response.State).IsEquivalentTo(expectedState);
    }
}
