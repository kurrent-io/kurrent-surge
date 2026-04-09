using Kurrent.Client.Streams;
using Sdk.TestKit.Fixtures;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
public class AppendRecordsTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.None)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    [Arguments<int>(2)]
    [Arguments<int>(5)]
    [Arguments<int>(8)]
    [Arguments<int>(11)]
    public async ValueTask creates_multiple_streams(int streams, CancellationToken ct) {
        // Arrange
        var builder = new AppendRecordsRequestBuilder();

        foreach (var i in Enumerable.Range(0, streams)) {
            var data = await Fixture.TestData.GenerateSet(ct);
            builder.WithRecords(data.Records.Take(1));
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
                new AppendRecordsRequestBuilder(), (builder, seededStream) => builder
                    .WithRecords(seededStream.Records.Take(1)) // just append the first record again...
                    .WithChecks(ConsistencyCheck.StreamAtRevision(seededStream.Stream, seededStream.Revision)))
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
