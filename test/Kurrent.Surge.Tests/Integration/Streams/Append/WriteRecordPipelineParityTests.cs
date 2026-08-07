using Bogus;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Experimental;
using Kurrent.Surge.Schema;
using Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Surge.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
[Timeout(30_000)]
public class WriteRecordPipelineParityTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    // Schema.Id is compared only when the registry supplied it: without one, both paths
    // fall back to a fresh Guid per mapping, so the ids differ by design.
    static async ValueTask AssertSameWireMessage(
        KurrentDB.Protocol.V2.Streams.AppendRecord actual,
        KurrentDB.Protocol.V2.Streams.AppendRecord expected
    ) {
        await Assert.That(actual.RecordId).IsEqualTo(expected.RecordId);
        await Assert.That(actual.Stream).IsEqualTo(expected.Stream);
        await Assert.That(actual.Schema.Name).IsEqualTo(expected.Schema.Name);
        await Assert.That(actual.Schema.Format).IsEqualTo(expected.Schema.Format);
        await Assert.That(actual.Data).IsEqualTo(expected.Data);
        await Assert.That(actual.Properties).IsEqualTo(expected.Properties);
    }

    [Test]
    public async ValueTask produces_the_same_wire_message_for_catalog_records(CancellationToken ct) {
        // Arrange
        var streams  = new StreamsClient(Fixture.Client);
        var mapping  = streams.CreateMappingContext(ct);
        var pipeline = new WriteRecordPipeline(mapping.SerializerProvider, mapping.Policy);

        var record = AppendRecord.Create(RaceCar.Generate(), r => {
            r.Stream = $"pipeline-parity-{TestId.New()}";
            r.Metadata.With("parity-key", Faker.Random.AlphaNumeric(12));
        });

        var expected = await record.ToContract(mapping);

        // Act
        var actual = await pipeline.Execute(record, ct);

        // Assert
        await AssertSameWireMessage(actual, expected);
    }

    [Test]
    public async ValueTask split_pipeline_produces_the_same_wire_message_for_catalog_records(CancellationToken ct) {
        // Arrange
        var streams  = new StreamsClient(Fixture.Client);
        var mapping  = streams.CreateMappingContext(ct);
        var pipeline = WriteRecordPipeline.Split(mapping.SerializerProvider, mapping.Policy);

        var record = AppendRecord.Create(RaceCar.Generate(), r => {
            r.Stream = $"pipeline-split-parity-{TestId.New()}";
            r.Metadata.With("parity-key", Faker.Random.AlphaNumeric(12));
        });

        var expected = await record.ToContract(mapping);

        // Act
        var actual = await pipeline.Execute(record, ct);

        // Assert
        await AssertSameWireMessage(actual, expected);
    }

    [Test]
    public async ValueTask split_pipeline_produces_the_same_wire_message_for_raw_bytes_records(CancellationToken ct) {
        // Arrange
        var streams  = new StreamsClient(Fixture.Client);
        var mapping  = streams.CreateMappingContext(ct);
        var pipeline = WriteRecordPipeline.Split(mapping.SerializerProvider, mapping.Policy);

        var record = AppendRecord.Create(Faker.Random.Bytes(64), r => {
            r.Stream     = $"pipeline-split-parity-bytes-{TestId.New()}";
            r.DataFormat = SchemaDataFormat.Bytes;
            r.SchemaName = "raw-bytes";
        });

        var expected = await record.ToContract(mapping);

        // Act
        var actual = await pipeline.Execute(record, ct);

        // Assert
        await AssertSameWireMessage(actual, expected);
    }

    [Test]
    public async ValueTask produces_the_same_wire_message_for_raw_bytes_records(CancellationToken ct) {
        // Arrange
        var streams  = new StreamsClient(Fixture.Client);
        var mapping  = streams.CreateMappingContext(ct);
        var pipeline = new WriteRecordPipeline(mapping.SerializerProvider, mapping.Policy);

        var record = AppendRecord.Create(Faker.Random.Bytes(64), r => {
            r.Stream     = $"pipeline-parity-bytes-{TestId.New()}";
            r.DataFormat = SchemaDataFormat.Bytes;
            r.SchemaName = "raw-bytes";
        });

        var expected = await record.ToContract(mapping);

        // Act
        var actual = await pipeline.Execute(record, ct);

        // Assert
        await AssertSameWireMessage(actual, expected);
    }
}
