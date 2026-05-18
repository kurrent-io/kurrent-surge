using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
public class AppendRecordsTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask creates_single_stream(CancellationToken ct) {
        // Arrange
        var streams = 1;
        
        var builder = new AppendRecordsRequest.Builder();
        
        foreach (var i in Enumerable.Range(0, streams)) {
            var data = await Fixture.TestData.GenerateSet(ct);
            builder.AddRecords(data.Records.Take(1));
        }

        var req = builder.Build();

        // Act
        var result = await Fixture.Client.Streams.AppendRecords(req, ct);

        // Assert
        await Assert.That(result.Position).IsGreaterThan(LogPosition.Earliest);
        await Assert.That(result.Revisions).Count().IsEqualTo(streams);
    }
    
    [Test]
    [Arguments<int>(1)]
    [Arguments<int>(3)]
    [Arguments<int>(5)]
    [Arguments<int>(7)]
    [Arguments<int>(9)]
    public async ValueTask creates_streams(int streams, CancellationToken ct) {
        // Arrange
        var builder = new AppendRecordsRequest.Builder();
        
        foreach (var i in Enumerable.Range(0, streams)) {
            var data = await Fixture.TestData.GenerateSet(ct);
            builder.AddRecords(data.Records.Take(1));
        }

        var req = builder.Build();

        // Act
        var result = await Fixture.Client.Streams.AppendRecords(req, ct);

        // Assert
        await Assert.That(result.Position).IsGreaterThan(LogPosition.Earliest);
        await Assert.That(result.Revisions).Count().IsEqualTo(streams);
    }

    [Test]
    [Arguments<int>(2)]
    [Arguments<int>(5)]
    [Arguments<int>(8)]
    [Arguments<int>(11)]
    public async ValueTask appends_to_existing_stream_with_expected_revision(int streams, CancellationToken ct) {
        // Arrange
        var seededStreams = await Fixture.SeedManyTestStreams(streams, ct);

        var req = seededStreams
            .Aggregate(
                new AppendRecordsRequest.Builder(), (builder, seededStream) => builder
                    .AddRecords(seededStream.Records.Take(1)) // just append the first record again...
                    .AddChecks(ConsistencyCheck.StreamAtRevision(seededStream.Stream, seededStream.Revision)))
            .Build();

        // Act
        var result = await Fixture.Client.Streams.AppendRecords(req, ct);

        // Assert
        await Assert.That(result.Revisions).Count().IsEqualTo(streams);

        foreach (var rev in result.Revisions) {
            var expectedRevision = seededStreams.Single(x => x.Stream == rev.Stream).Revision + 1;
            await Assert.That(rev).Member(x => x.Revision, x => x.IsEqualTo(expectedRevision));
        }
    }
}
