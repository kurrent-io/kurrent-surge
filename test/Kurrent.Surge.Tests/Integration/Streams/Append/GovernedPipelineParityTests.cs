using Bogus;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Experimental;
using Kurrent.Surge.Schema;
using Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Surge.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
[Timeout(30_000)]
public class GovernedPipelineParityTests {
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
    public async ValueTask governed_write_produces_the_same_wire_message_for_catalog_records(CancellationToken ct) {
        // Arrange
        var streams  = new StreamsClient(Fixture.Client);
        var mapping  = streams.CreateMappingContext(ct);
        var pipeline = GovernedWriteRecordPipeline.From(mapping.SerializerProvider, mapping.Policy, SchemaGovernance.Create(Fixture.Client));

        var record = AppendRecord.Create(RaceCar.Generate(), r => {
            r.Stream = $"governed-parity-{TestId.New()}";
            r.Metadata.With("parity-key", Faker.Random.AlphaNumeric(12));
        });

        var expected = await record.ToContract(mapping);

        // Act
        var actual = await pipeline.Execute(record, ct);

        // Assert
        await AssertSameWireMessage(actual, expected);
    }

    [Test]
    public async ValueTask governed_write_produces_the_same_wire_message_for_raw_bytes_records(CancellationToken ct) {
        // Arrange
        var streams  = new StreamsClient(Fixture.Client);
        var mapping  = streams.CreateMappingContext(ct);
        var pipeline = GovernedWriteRecordPipeline.From(mapping.SerializerProvider, mapping.Policy, SchemaGovernance.Create(Fixture.Client));

        var record = AppendRecord.Create(Faker.Random.Bytes(64), r => {
            r.Stream     = $"governed-parity-bytes-{TestId.New()}";
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
    public async ValueTask governed_read_decodes_the_same_value_as_the_production_decoder(CancellationToken ct) {
        // Arrange — the production read decodes the baseline copy; the governed pipeline
        // decodes a SkipDecoding copy of the same record.
        var streams = new StreamsClient(Fixture.Client);
        var mapping = streams.CreateMappingContext(ct);
        var data    = await Fixture.SeedTestStream(1, ct);

        var decoded = await Fixture.Client.Streams
            .ReadStream(data.Stream, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        var raw = await Fixture.Client.Streams
            .ReadStream(data.Stream, new ReadStreamOptions { SkipDecoding = true }, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        var pipeline = GovernedReadRecordPipeline.From(mapping.SerializerProvider, mapping.Policy, SchemaGovernance.Create(Fixture.Client));

        // Act
        var outcome = await pipeline.Execute(raw[0], ct);

        // Assert
        await Assert.That(outcome).IsEqualTo(DecodeOutcome.Decoded);
        await Assert.That(raw[0].ValueType).IsEqualTo(decoded[0].ValueType);
        await Assert.That(raw[0].Value).IsEquivalentTo(decoded[0].Value);
    }

    [Test]
    public async ValueTask governed_read_reports_unavailable_for_raw_bytes_records(CancellationToken ct) {
        // Arrange — raw bytes never decode: Data already exposes the payload.
        var streams    = new StreamsClient(Fixture.Client);
        var mapping    = streams.CreateMappingContext(ct);
        var stream     = (await Fixture.TestData.GenerateSet(1, ct)).Stream;
        var schemaName = $"raw-bytes-{TestId.New()}";

        var record = AppendRecord.Create(Faker.Random.Bytes(32), r => {
            r.Stream     = stream;
            r.DataFormat = SchemaDataFormat.Bytes;
            r.SchemaName = schemaName;
        });

        await Fixture.Client.Streams.AppendRecords([record], ct);

        var raw = await Fixture.Client.Streams
            .ReadStream(stream, new ReadStreamOptions { SkipDecoding = true }, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        var pipeline = GovernedReadRecordPipeline.From(mapping.SerializerProvider, mapping.Policy, SchemaGovernance.Create(Fixture.Client));

        // Act
        var outcome = await pipeline.Execute(raw[0], ct);

        // Assert
        await Assert.That(outcome).IsEqualTo(DecodeOutcome.Unavailable);
        await Assert.That(raw[0].IsDecoded).IsFalse();
    }

    [Test]
    public async ValueTask adapter_governed_write_produces_the_same_wire_message_for_catalog_records(CancellationToken ct) {
        // Arrange — no governance passed: the pipeline falls back to the SchemaManager adapter.
        var streams  = new StreamsClient(Fixture.Client);
        var mapping  = streams.CreateMappingContext(ct);
        var pipeline = GovernedWriteRecordPipeline.From(mapping.SerializerProvider, mapping.Policy);

        var record = AppendRecord.Create(RaceCar.Generate(), r => {
            r.Stream = $"governed-adapter-parity-{TestId.New()}";
            r.Metadata.With("parity-key", Faker.Random.AlphaNumeric(12));
        });

        var expected = await record.ToContract(mapping);

        // Act
        var actual = await pipeline.Execute(record, ct);

        // Assert
        await AssertSameWireMessage(actual, expected);
    }
}
