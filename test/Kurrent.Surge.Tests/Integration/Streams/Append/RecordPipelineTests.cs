using System.Diagnostics;
using Bogus;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Governance;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Schema.Serialization.Json;
using Kurrent.Surge.Client.Schema.Serialization.Protobuf;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Surge.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
[Timeout(30_000)]
public class RecordPipelineTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    // Self-composed, equivalent to the fixture client's own defaults — the pipeline-composition
    // seam on KurrentClient is gone, so tests build their own resolver/schema/codecs
    // instead of borrowing the client's. This is pipeline semantics under test, not composition.
    static readonly List<MessageCatalog> Catalogs =
        MessageCatalogDiscovery.GetCatalogs().Where(catalog => catalog.Types.Contains(typeof(RaceCar))).ToList();

    static readonly MessageResolver Resolver = MessageResolver.From(Catalogs);

    static readonly KurrentClientSchemaOptions Schema = KurrentClientSchemaOptions.Disabled;

    static readonly SchemaCodecProvider Codecs = new([new JsonSchemaCodec(), new ProtobufSchemaCodec()]);

    SchemaGovernance CreateGovernance(SchemaRegistryPolicy policy) {
        var resolved = policy
            .Resolve(Schema, null, SchemaDataFormat.Json)
            .EnsureDataFormatCompliance(SchemaDataFormat.Json);

        return SchemaGovernance.Create(Fixture.Client, Catalogs, resolved);
    }

    // The frame invariants shared by every catalog-record write case: no minted id, the
    // resolved (or overridden) schema name, the record's own id/stream/format carried through
    // untouched. Payload correctness belongs to the round-trip test — comparing bytes against
    // the output of the same codec that produced them here would be a tautology, so a
    // non-empty check is enough.
    static async ValueTask AssertCatalogRecordFrame(Contracts.AppendRecord actual, AppendRecord record, string expectedSchemaName) {
        await Assert.That(actual.RecordId).IsEqualTo(record.RecordId.ToGuidString());
        await Assert.That(actual.Stream).IsEqualTo(record.Stream);
        await Assert.That(actual.Schema.Id).IsEmpty();
        await Assert.That(actual.Schema.Name).IsEqualTo(expectedSchemaName);
        await Assert.That(actual.Schema.Format).IsEqualTo((Contracts.SchemaFormat)record.DataFormat);
        await Assert.That(actual.Data.Length).IsGreaterThan(0);
    }

    [Test]
    public async ValueTask governed_write_cancellation_abandons_a_pending_catalog_validation(CancellationToken ct) {
        // Arrange — a catalog registration that never completes: the caller's token must
        // abandon the wait instead of blocking until the client lifetime ends.
        var streams = new StreamsClient(Fixture.Client);

        var pipeline = WriteRecordPipeline.Create(Resolver, streams.RegistryPolicy, Schema, Codecs, new PendingGovernance());

        var record = AppendRecord.Create(RaceCar.Generate(), r => r.Stream = $"validation-cancel-{TestId.New()}");

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(250));

        // Act & Assert — cancellation surfaces as cancellation, not as a mapping failure.
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await pipeline.Execute(record, cancellation.Token));
    }

    [Test]
    public async ValueTask governed_write_frames_a_catalog_record_with_the_resolved_schema_name(CancellationToken ct) {
        // Arrange
        var streams  = new StreamsClient(Fixture.Client);
        var policy   = streams.RegistryPolicy;
        var pipeline = WriteRecordPipeline.Create(
            Resolver, policy, Schema, Codecs, CreateGovernance(policy));

        var metadataValue = Faker.Random.AlphaNumeric(12);

        var record = AppendRecord.Create(RaceCar.Generate(), r => {
            r.Stream = $"governed-parity-{TestId.New()}";
            r.Metadata.With("parity-key", metadataValue);
        });

        var expectedSchemaName = Resolver.ResolveSchemaName(record.Value!.GetType()).Value;

        // Act
        var actual = await pipeline.Execute(record, ct);

        // Assert
        await AssertCatalogRecordFrame(actual, record, expectedSchemaName);
        await Assert.That(actual.Properties["parity-key"].StringValue).IsEqualTo(metadataValue);
    }

    [Test]
    public async ValueTask governed_write_honors_an_explicit_schema_name_override_for_catalog_records(CancellationToken ct) {
        // Arrange
        var streams  = new StreamsClient(Fixture.Client);
        var policy   = streams.RegistryPolicy;
        var pipeline = WriteRecordPipeline.Create(
            Resolver, policy, Schema, Codecs, CreateGovernance(policy));

        var schemaNameOverride = $"parity-override-{TestId.New()}";

        var record = AppendRecord.Create(RaceCar.Generate(), r => {
            r.Stream     = $"governed-parity-{TestId.New()}";
            r.SchemaName = schemaNameOverride;
        });

        // Act
        var actual = await pipeline.Execute(record, ct);

        // Assert — the explicit override wins over the catalog-resolved name.
        await AssertCatalogRecordFrame(actual, record, schemaNameOverride);
    }

    [Test]
    public async ValueTask governed_write_frames_a_raw_bytes_record_with_its_exact_payload(CancellationToken ct) {
        // Arrange
        var streams  = new StreamsClient(Fixture.Client);
        var policy   = streams.RegistryPolicy;
        var pipeline = WriteRecordPipeline.Create(
            Resolver, policy, Schema, Codecs, CreateGovernance(policy));

        var payload    = Faker.Random.Bytes(64);
        var schemaName = $"raw-bytes-{TestId.New()}";

        var record = AppendRecord.Create(payload, r => {
            r.Stream     = $"governed-parity-bytes-{TestId.New()}";
            r.DataFormat = SchemaDataFormat.Bytes;
            r.SchemaName = schemaName;
        });

        // Act
        var actual = await pipeline.Execute(record, ct);

        // Assert
        await Assert.That(actual.RecordId).IsEqualTo(record.RecordId.ToGuidString());
        await Assert.That(actual.Stream).IsEqualTo(record.Stream);
        await Assert.That(actual.Schema.Id).IsEmpty();
        await Assert.That(actual.Schema.Name).IsEqualTo(schemaName);
        await Assert.That(actual.Schema.Format).IsEqualTo((Contracts.SchemaFormat)SchemaDataFormat.Bytes);
        await Assert.That(actual.Data.ToByteArray()).IsEquivalentTo(payload);
    }

    [Test]
    public async ValueTask read_pipeline_decodes_appended_record_to_the_original_value(CancellationToken ct) {
        // Arrange
        var streams = new StreamsClient(Fixture.Client);
        var policy  = streams.RegistryPolicy;
        var data    = await Fixture.SeedTestStream(1, ct);

        var raw = await Fixture.Client.Streams
            .ReadStream(data.Stream, new ReadStreamOptions { SkipDecoding = true }, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        var pipeline = ReadRecordPipeline.Create(
            Resolver, policy, Schema, Codecs, CreateGovernance(policy));

        // Act
        var outcome = await pipeline.Execute(raw[0], ct);

        // Assert — decoded value matches the message that was originally arranged, before it
        // was ever serialized.
        var original = data.Records[0].Value!;

        await Assert.That(outcome).IsEqualTo(DecodeOutcome.Decoded);
        await Assert.That(raw[0].ValueType).IsEqualTo(original.GetType());
        await Assert.That(raw[0].Value).IsEquivalentTo(original);
    }

    [Test]
    public async ValueTask governed_read_reports_unavailable_for_raw_bytes_records(CancellationToken ct) {
        // Arrange — raw bytes never decode: Data already exposes the payload.
        var streams    = new StreamsClient(Fixture.Client);
        var policy     = streams.RegistryPolicy;
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

        var pipeline = ReadRecordPipeline.Create(
            Resolver, policy, Schema, Codecs, CreateGovernance(policy));

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
        var policy   = streams.RegistryPolicy;
        var pipeline = WriteRecordPipeline.Create(
            Resolver, policy, Schema, Codecs, CreateGovernance(policy));

        var record   = AppendRecord.Create(RaceCar.Generate(), r => r.Stream = $"governed-wire-{TestId.New()}");
        var contract = await pipeline.Execute(record, ct);

        var request = new KurrentDB.Protocol.V2.Streams.AppendRecordsRequest();
        request.Records.Add(contract);

        // Act — raw service call, bypassing the AppendRecords client path entirely.
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
        var streams    = new StreamsClient(Fixture.Client);
        var policy     = streams.RegistryPolicy;
        var governance = CreateGovernance(policy);

        var write = WriteRecordPipeline.Create(Resolver, policy, Schema, Codecs, governance);
        var read  = ReadRecordPipeline.Create(Resolver, policy, Schema, Codecs, governance);

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

    // A catalog registration that never completes; the govern step's wait must be abandonable.
    sealed class PendingGovernance : ISchemaGovernance {
        public Task<CatalogSchemaMap> CatalogRegistration { get; } = new TaskCompletionSource<CatalogSchemaMap>().Task;

        public void StartCatalogRegistration() { }

        public ValueTask<SchemaVersionId> EnsureRegistered(SchemaName schema, Type messageType, ResolvedSchemaRegistryPolicy policy, CancellationToken ct) =>
            throw new UnreachableException();

        public ValueTask<SchemaVersionId> EnsureCompatible(RecordSchemaInfo schemaInfo, Type messageType, ResolvedSchemaRegistryPolicy policy, CancellationToken ct) =>
            throw new UnreachableException();
    }
}
