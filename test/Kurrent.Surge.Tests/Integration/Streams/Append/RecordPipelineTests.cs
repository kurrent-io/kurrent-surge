using System.Diagnostics;
using Bogus;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Schema.Governance;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Codecs;
using Kurrent.Surge.Codecs.Avro;
using Kurrent.Surge.Codecs.Json;
using Kurrent.Surge.Codecs.Protobuf;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Surge.Schema.Naming;
using Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;
using Kurrent.Testkit.TUnit;
using Contracts = KurrentDB.Protocol.V2.Streams;
using Protocol = Kurrent.Surge.Tests.Client.LoadBalancing.FakeCluster.Protocol;
using ValidationException = Kurrent.Surge.Validation.ValidationException;

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

    // Mirrors the set KurrentClient composes, so a resolution that picks Avro finds its codec.
    static readonly SchemaCodecProvider Codecs = new([new JsonSchemaCodec(), new ProtobufSchemaCodec(), new AvroSchemaCodec()]);

    SchemaGovernance CreateGovernance() =>
        SchemaGovernance.Create(Fixture.Client, Catalogs, Schema);

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
        var pipeline = WriteRecordPipeline.Create(Resolver, Schema, Codecs, new PendingGovernance());

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
        var pipeline = WriteRecordPipeline.Create(Resolver, Schema, Codecs, CreateGovernance());

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
    public async ValueTask governed_write_rejects_a_schema_name_on_a_catalog_record(CancellationToken ct) {
        // Arrange — the name belongs to raw payloads. A catalog record takes the catalog's name,
        // so accepting one here would frame a name and a schema id describing different schemas.
        var pipeline = WriteRecordPipeline.Create(Resolver, Schema, Codecs, CreateGovernance());

        var record = AppendRecord.Create(RaceCar.Generate(), r => {
            r.Stream     = $"governed-parity-{TestId.New()}";
            r.SchemaName = $"parity-override-{TestId.New()}";
        });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(async () => await pipeline.Execute(record, ct));

        await Assert.That(ex?.Errors).Contains(p => p.FieldName == nameof(AppendRecord.SchemaName));
    }

    [Test]
    public async ValueTask governed_write_frames_a_raw_bytes_record_with_its_exact_payload(CancellationToken ct) {
        // Arrange
        var pipeline = WriteRecordPipeline.Create(Resolver, Schema, Codecs, CreateGovernance());

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
        var data = await Fixture.SeedTestStream(1, ct);

        var raw = await Fixture.Client.Streams
            .ReadStream(data.Stream, new ReadStreamOptions { SkipDecoding = true }, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        var pipeline = ReadRecordPipeline.Create(Resolver, Schema, Codecs, CreateGovernance());

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

        var pipeline = ReadRecordPipeline.Create(Resolver, Schema, Codecs, CreateGovernance());

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
        var pipeline = WriteRecordPipeline.Create(Resolver, Schema, Codecs, CreateGovernance());

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
        var governance = CreateGovernance();

        var write = WriteRecordPipeline.Create(Resolver, Schema, Codecs, governance);
        var read  = ReadRecordPipeline.Create(Resolver, Schema, Codecs, governance);

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

    #region ->> Non-Json framing and encode failures <<-

    [Test]
    public async ValueTask frames_a_protobuf_record_with_protobuf_not_the_registration_format(CancellationToken ct) {
        // Arrange — registry ON, so the catalog map holds an entry for the type. That entry is
        // registered as Json, because the registry stores Json definitions only. The record is
        // Protobuf. The frame must carry the record's format, not the registration's.
        var schema       = KurrentClientSchemaOptions.AutoRegister;
        var nameStrategy = SchemaNameStrategies.Namespace($"proto-frame-{Guid.NewGuid():N}");
        var catalog      = new MessageCatalog("proto-frame").Map<Protocol.Empty>(nameStrategy);
        var resolver     = MessageResolver.From(catalog);
        var governance   = SchemaGovernance.Create(Fixture.Client, [catalog], schema, ct);
        var pipeline     = WriteRecordPipeline.Create(resolver, schema, Codecs, governance);

        // Unstated, so the type decides: Empty is an IMessage, which resolves to Protobuf.
        var record = AppendRecord.Create(new Protocol.Empty(), r => r.Stream = $"proto-frame-{TestId.New()}");

        // Act
        var actual = await pipeline.Execute(record, ct);

        // Assert — the record settled on Protobuf, the frame agrees, and the schema id is
        // present, which proves the type-keyed registration lookup hit rather than missing.
        await Assert.That(record.DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
        await Assert.That(actual.Schema.Format).IsEqualTo((Contracts.SchemaFormat)SchemaDataFormat.Protobuf);
        await Assert.That(actual.Schema.Id).IsNotEmpty();
    }

    // RaceCar is a plain type: not a Protobuf message and no Avro codec is generated for it.
    public sealed class FormatsAPlainTypeCannotSatisfy : TestCaseDataGenerator<SchemaDataFormat> {
        protected override IEnumerable<TestCase<SchemaDataFormat>> Data() {
            yield return new(SchemaDataFormat.Protobuf, DisplayName: "protobuf");
            yield return new(SchemaDataFormat.Avro, DisplayName: "avro");
        }
    }

    [Test]
    [FormatsAPlainTypeCannotSatisfy]
    public async ValueTask a_stated_format_the_type_cannot_satisfy_fails_at_encode(
        SchemaDataFormat stated, CancellationToken ct
    ) {
        // Arrange — a stated format is authoritative and is never narrowed, so an impossible
        // one reaches the codec and fails there rather than being silently corrected.
        var pipeline = WriteRecordPipeline.Create(Resolver, Schema, Codecs, CreateGovernance());

        var record = AppendRecord.Create(RaceCar.Generate(), r => {
            r.Stream     = $"encode-fail-stated-{TestId.New()}";
            r.DataFormat = stated;
        });

        // Act & Assert
        await Assert.ThrowsAsync<SchemaCodecEncodingException>(async () => await pipeline.Execute(record, ct));
    }

    // Exists solely so a type resolves to Avro. AvroCodecRegistry is process-wide with no
    // removal, so nothing else references this type. The override sends it down the Json codec,
    // so the registered delegates are never invoked.
    sealed record AvroNative(string Value);

    // The two native formats the override has something to override. A plain type is already
    // Json, so it would prove nothing.
    public sealed class NonJsonNativeFormats : TestCaseDataGenerator<SchemaDataFormat> {
        protected override IEnumerable<TestCase<SchemaDataFormat>> Data() {
            yield return new(SchemaDataFormat.Protobuf, DisplayName: "protobuf");
            yield return new(SchemaDataFormat.Avro, DisplayName: "avro");
        }
    }

    [Test]
    [NonJsonNativeFormats]
    public async ValueTask append_as_json_overrides_a_non_json_native_format(
        SchemaDataFormat native, CancellationToken ct
    ) {
        // Arrange
        object NewValue() {
            if (native != SchemaDataFormat.Avro)
                return new Protocol.Empty();

            AvroCodecRegistry.Register(
                typeof(AvroNative), static (_, _) => { }, static _ => new AvroNative("x"));

            return new AvroNative("x");
        }

        var value    = NewValue();
        var catalog  = new MessageCatalog("json-override").Map($"json-override-{native}", value.GetType());
        var resolver = MessageResolver.From(catalog);

        WriteRecordPipeline PipelineFor(KurrentClientSchemaOptions schema) =>
            WriteRecordPipeline.Create(
                resolver, schema, Codecs, SchemaGovernance.Create(Fixture.Client, [catalog], schema, ct));

        AppendRecord NewRecord() =>
            AppendRecord.Create(value, r => r.Stream = $"json-override-{TestId.New()}");

        // Act — control first: with the override off the type decides, which proves the value
        // really is native to something other than Json.
        var untouched  = NewRecord();
        var controlled = await PipelineFor(Schema).Execute(untouched, ct);

        var overridden = NewRecord();
        var actual     = await PipelineFor(new KurrentClientSchemaOptions { AppendAsJson = true })
            .Execute(overridden, ct);

        // Assert
        await Assert.That(untouched.DataFormat).IsEqualTo(native);
        await Assert.That(controlled.Schema.Format).IsEqualTo((Contracts.SchemaFormat)native);

        await Assert.That(overridden.DataFormat).IsEqualTo(SchemaDataFormat.Json);
        await Assert.That(actual.Schema.Format).IsEqualTo((Contracts.SchemaFormat)SchemaDataFormat.Json);
    }

    #endregion // Non-Json framing and encode failures

    // A catalog registration that never completes; the govern step's wait must be abandonable.
    sealed class PendingGovernance : ISchemaGovernance {
        public Task<CatalogSchemaMap> CatalogRegistration { get; } = new TaskCompletionSource<CatalogSchemaMap>().Task;

        public ValueTask<SchemaVersionId> EnsureRegistered(SchemaName schema, Type messageType, CancellationToken ct) =>
            throw new UnreachableException();

        public ValueTask<SchemaVersionId> EnsureCompatible(RecordSchemaInfo schemaInfo, Type messageType, CancellationToken ct) =>
            throw new UnreachableException();
    }
}
