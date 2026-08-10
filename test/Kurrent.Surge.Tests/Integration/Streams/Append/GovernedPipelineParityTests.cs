using Bogus;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Experimental;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
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

    CatalogValidationInterceptor CreateValidation(MappingContext mapping, ISchemaGovernance? governance = null) {
        var (_, _, registrations) = SchemaManagerGovernanceAdapter.Extract(mapping.SerializerProvider);

        var catalogs = MessageCatalogDiscovery.GetCatalogs()
            .Where(catalog => catalog.Types.Contains(typeof(RaceCar)))
            .ToList();

        var resolved = mapping.Policy
            .Resolve(registrations[SchemaDataFormat.Json], null, SchemaDataFormat.Json)
            .EnsureDataFormatCompliance(SchemaDataFormat.Json);

        return new CatalogValidationInterceptor(governance ?? SchemaGovernance.Create(Fixture.Client), catalogs, resolved);
    }

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
        var pipeline = GovernedWriteRecordPipeline.From(mapping.SerializerProvider, mapping.Policy, CreateValidation(mapping));

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
        var pipeline = GovernedWriteRecordPipeline.From(mapping.SerializerProvider, mapping.Policy, CreateValidation(mapping));

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
    public async ValueTask server_accepts_a_governed_record_with_absent_schema_id(CancellationToken ct) {
        // Arrange — a governed wire record: SchemaInfo.Id deliberately absent (the per-record
        // Guid fallback was a bug). The server has never received one before this test.
        var streams  = new StreamsClient(Fixture.Client);
        var mapping  = streams.CreateMappingContext(ct);
        var pipeline = GovernedWriteRecordPipeline.From(mapping.SerializerProvider, mapping.Policy, CreateValidation(mapping));

        var record   = AppendRecord.Create(RaceCar.Generate(), r => r.Stream = $"governed-wire-{TestId.New()}");
        var contract = await pipeline.Execute(record, ct);

        var request = new KurrentDB.Protocol.V2.Streams.AppendRecordsRequest();
        request.Records.Add(contract);

        // Act — raw service call, bypassing the ToContract mapping entirely.
        await streams.ServiceClient.AppendRecordsAsync(request, cancellationToken: ct);

        // Assert — the id really was absent, the server accepted, and the record reads back.
        await Assert.That(contract.Schema.Id).IsEmpty();

        var read = await Fixture.Client.Streams
            .ReadStream(contract.Stream, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(read).Count().IsEqualTo(1);
        await Assert.That(read[0].Schema.Schema.Value).IsEqualTo(contract.Schema.Name);
    }

    [Test]
    public async ValueTask governed_write_and_read_round_trip_one_hundred_records(CancellationToken ct) {
        // Arrange — 100 catalog records through the FULL governed path: write chain frames
        // them (absent SchemaInfo.Id), raw V2 append sends them, governed read chain decodes
        // what the server returns.
        var streams  = new StreamsClient(Fixture.Client);
        var mapping  = streams.CreateMappingContext(ct);
        var governance = SchemaGovernance.Create(Fixture.Client);

        var write = GovernedWriteRecordPipeline.From(mapping.SerializerProvider, mapping.Policy, CreateValidation(mapping, governance));
        var read  = GovernedReadRecordPipeline.From(mapping.SerializerProvider, mapping.Policy, governance);

        var stream   = $"governed-roundtrip-{TestId.New()}";
        var expected = Enumerable.Range(0, 100).Select(_ => RaceCar.Generate()).ToList();

        var request = new KurrentDB.Protocol.V2.Streams.AppendRecordsRequest();
        foreach (var car in expected)
            request.Records.Add(await write.Execute(AppendRecord.Create(car, r => r.Stream = stream), ct));

        // Act — one raw append, then read back undecoded and decode through the governed chain.
        await streams.ServiceClient.AppendRecordsAsync(request, cancellationToken: ct);

        var records = await Fixture.Client.Streams
            .ReadStream(stream, new ReadStreamOptions { SkipDecoding = true }, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert — every record decodes and every value survives the round trip in order.
        await Assert.That(records).Count().IsEqualTo(expected.Count);

        for (var i = 0; i < records.Count; i++) {
            var outcome = await read.Execute(records[i], ct);

            await Assert.That(outcome).IsEqualTo(DecodeOutcome.Decoded);
            await Assert.That(records[i].Value).IsEquivalentTo(expected[i]);
        }
    }

    [Test]
    public async ValueTask adapter_governed_write_produces_the_same_wire_message_for_catalog_records(CancellationToken ct) {
        // Arrange — validation backed by the SchemaManager adapter instead of real governance.
        var streams  = new StreamsClient(Fixture.Client);
        var mapping  = streams.CreateMappingContext(ct);

        var (manager, _, _) = SchemaManagerGovernanceAdapter.Extract(mapping.SerializerProvider);

        var pipeline = GovernedWriteRecordPipeline.From(
            mapping.SerializerProvider, mapping.Policy, CreateValidation(mapping, new SchemaManagerGovernanceAdapter(manager)));

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
