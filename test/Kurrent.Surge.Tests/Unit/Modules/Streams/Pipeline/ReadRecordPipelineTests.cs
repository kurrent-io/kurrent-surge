using System.Diagnostics;
using System.Text;
using Bogus;
using Kurrent.Surge.Schema.Governance;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Codecs;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Tests.Modules.Streams.Pipeline;

// $> link records carry "{revision}@{stream}" text as their Bytes payload — a wire hazard the
// bytes serializer this pipeline replaced used to special-case. Every other Bytes record has
// nothing to decode.
[Category("Streams")]
public class ReadRecordPipelineTests {
    static readonly Faker Faker = new();

    static ReadRecordPipeline NewPipeline() =>
        ReadRecordPipeline.Create(
            MessageResolver.From(),
            KurrentClientSchemaOptions.Disabled,
            new SchemaCodecProvider([]),
            new UnreachableGovernance());

    static Record NewBytesRecord(SchemaName schemaName, ReadOnlyMemory<byte> data) =>
        new() {
            Schema = new RecordSchemaInfo { Schema = schemaName, DataFormat = SchemaDataFormat.Bytes },
            Data   = data
        };

    [Test]
    public async ValueTask read_pipeline_decodes_link_records_to_link_values() {
        // Arrange
        var expected = new LinkTo(StreamName.From(Faker.Lorem.Slug()), Revision.From(Faker.Random.Long(0, 1_000_000)));
        var payload  = Encoding.UTF8.GetBytes($"{expected.Revision.Value}@{expected.Stream.Value}");
        var record   = NewBytesRecord(LinkTo.SchemaName, payload);

        var sut = NewPipeline();

        // Act
        var outcome = await sut.Execute(record, CancellationToken.None);

        // Assert
        await Assert.That(outcome).IsEqualTo(DecodeOutcome.Decoded);
        await Assert.That(record.ValueType).IsEqualTo(typeof(LinkTo));
        await Assert.That(record.Value).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask read_pipeline_reports_unavailable_for_non_link_bytes_records() {
        // Arrange — an ordinary Bytes record, unrelated schema name.
        var record = NewBytesRecord(SchemaName.From(Faker.Lorem.Slug()), Faker.Random.Bytes(16));

        var sut = NewPipeline();

        // Act
        var outcome = await sut.Execute(record, CancellationToken.None);

        // Assert
        await Assert.That(outcome).IsEqualTo(DecodeOutcome.Unavailable);
        await Assert.That(record.IsDecoded).IsFalse();
    }

    [Test]
    public async ValueTask read_pipeline_wraps_malformed_link_payload_in_deserialization_exception() {
        // Arrange — no '@' separator, so LinkTo.Parse throws FormatException.
        var record = NewBytesRecord(LinkTo.SchemaName, Encoding.UTF8.GetBytes(Faker.Lorem.Word()));

        var sut = NewPipeline();

        // Act & Assert
        await Assert.That(async () => await sut.Execute(record, CancellationToken.None))
            .Throws<SchemaCodecDecodingException>();
    }

    // Never reached: the LinkTo/Bytes guard in Execute short-circuits before governance runs.
    // The catalog registration is completed-and-empty rather than throwing because the govern
    // step would await it before any other member.
    sealed class UnreachableGovernance : ISchemaGovernance {
        public Task<CatalogSchemaMap> CatalogRegistration { get; } = Task.FromResult(CatalogSchemaMap.Empty);

        public ValueTask<SchemaVersionId> EnsureRegistered(SchemaName schema, Type messageType, CancellationToken ct) =>
            throw new UnreachableException();

        public ValueTask<SchemaVersionId> EnsureCompatible(RecordSchemaInfo schemaInfo, Type messageType, CancellationToken ct) =>
            throw new UnreachableException();
    }
}
