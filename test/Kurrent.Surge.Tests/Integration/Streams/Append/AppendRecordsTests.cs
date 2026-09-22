using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
[Timeout(10_000)]
public class AppendRecordsTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask appends_raw_bytes_record_with_explicit_schema_name(CancellationToken ct) {
        // Arrange — a raw byte[] payload has no type mapping to resolve a schema name from, and
        // the server rejects an empty name; the explicit SchemaName is what makes it appendable.
        var stream     = (await Fixture.TestData.GenerateSet(1, ct)).Stream;
        var schemaName = $"raw-bytes-{TestId.New()}";

        var record = AppendRecord.Create(Fixture.Faker.Random.Bytes(32), r => r with {
            Stream     = stream,
            DataFormat = SchemaDataFormat.Bytes,
            SchemaName = schemaName
        });

        // Act
        var result = await Fixture.Client.Streams.AppendRecords([record], ct);

        // Assert — accepted by the server, and the explicit name round-trips on the read
        await Assert.That(result.Position).IsGreaterThan(LogPosition.Earliest);

        var read = await Fixture.Client.Streams
            .ReadStream(stream, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(read).Count().IsEqualTo(1);
        await Assert.That(read[0].Schema.Schema).IsEqualTo(schemaName);
    }

    [Test]
    public async ValueTask creates_single_stream(CancellationToken ct) {
        // Arrange
        var streams = 1;

        var records = new List<AppendRecord>();

        foreach (var i in Enumerable.Range(0, streams)) {
            var data = await Fixture.TestData.GenerateSet(ct);
            records.AddRange(data.Records.Take(1));
        }

        // Act
        var result = await Fixture.Client.Streams.AppendRecords(records, ct);

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
        var records = new List<AppendRecord>();

        foreach (var i in Enumerable.Range(0, streams)) {
            var data = await Fixture.TestData.GenerateSet(ct);
            records.AddRange(data.Records.Take(1));
        }

        // Act
        var result = await Fixture.Client.Streams.AppendRecords(records, ct);

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

        var records = seededStreams
            .SelectMany(seededStream => seededStream.Records.Take(1)) // just append the first record again...
            .ToArray();

        var options = new AppendRecordsOptions()
            .AddChecks(seededStreams
                .Select(seededStream => (ConsistencyCheck)ConsistencyCheck.StreamAtRevision(seededStream.Stream, seededStream.Revision))
                .ToArray());

        // Act
        var result = await Fixture.Client.Streams.AppendRecords(records, options, ct);

        // Assert
        await Assert.That(result.Revisions).Count().IsEqualTo(streams);

        foreach (var rev in result.Revisions) {
            var expectedRevision = seededStreams.Single(x => x.Stream == rev.Stream).Revision + 1;
            await Assert.That(rev).Member(x => x.Revision, x => x.IsEqualTo(expectedRevision));
        }
    }
}
