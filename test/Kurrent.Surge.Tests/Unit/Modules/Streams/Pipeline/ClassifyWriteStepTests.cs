using System.Buffers;
using Bogus;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Codecs.Avro;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Testkit.TUnit;
using Protocol = Kurrent.Surge.Tests.Client.LoadBalancing.FakeCluster.Protocol;

namespace Kurrent.Surge.Tests.Modules.Streams.Pipeline;

// Bytes records skip the codec entirely and unwrap through this step — kept in sync with the
// shapes BytesPassthroughSerializer used to accept before the governed pipeline replaced it.
[Category("Streams")]
public class ClassifyWriteStepTests {
    static readonly Faker Faker = new();

    static async ValueTask<ReadOnlyMemory<byte>> Classify(object value) {
        var sut = new ClassifyWriteStep(MessageResolver.From(), appendAsJson: false);

        var record = AppendRecord.Create(value, r => r with {
            Stream     = "classify-write-step-tests",
            DataFormat = SchemaDataFormat.Bytes,
            SchemaName = "classify-write-step-schema"
        });

        var resolution = await sut.Execute(record, CancellationToken.None);

        return resolution.Match(
            static _ => throw new InvalidOperationException("Expected an encoded (bytes) resolution."),
            static encoded => encoded.Payload);
    }

    [Test]
    public async ValueTask unwraps_byte_array() {
        // Arrange
        var bytes = Faker.Random.Bytes(16);

        // Act
        var result = await Classify(bytes);

        // Assert
        await Assert.That(result.ToArray()).IsEquivalentTo(bytes);
    }

    [Test]
    public async ValueTask unwraps_readonly_memory() {
        // Arrange
        var bytes = Faker.Random.Bytes(16);

        // Act
        var result = await Classify((ReadOnlyMemory<byte>)bytes);

        // Assert
        await Assert.That(result.ToArray()).IsEquivalentTo(bytes);
    }

    [Test]
    public async ValueTask unwraps_memory() {
        // Arrange
        var bytes = Faker.Random.Bytes(16);

        // Act
        var result = await Classify((Memory<byte>)bytes);

        // Assert
        await Assert.That(result.ToArray()).IsEquivalentTo(bytes);
    }

    [Test]
    public async ValueTask unwraps_readonly_sequence() {
        // Arrange
        var bytes = Faker.Random.Bytes(16);

        // Act
        var result = await Classify(new ReadOnlySequence<byte>(bytes));

        // Assert
        await Assert.That(result.ToArray()).IsEquivalentTo(bytes);
    }

    [Test]
    public async ValueTask unwraps_array_segment() {
        // Arrange
        var bytes = Faker.Random.Bytes(16);

        // Act
        var result = await Classify(new ArraySegment<byte>(bytes));

        // Assert
        await Assert.That(result.ToArray()).IsEquivalentTo(bytes);
    }

    #region ->> Format resolution <<-

    // Neither Avro-generated nor a Protobuf message, so its own format is JSON.
    sealed record Payload(string Value);

    // Made Avro-capable by registering a codec entry. The registry is process-wide with no
    // removal, so this type exists solely for these tests and is referenced nowhere else.
    sealed record AvroPayload(string Value);

    /// <summary>
    /// Runs the step and returns the format it settled on, read off the resolution the step
    /// returns.
    /// </summary>
    static async ValueTask<SchemaDataFormat> Resolve(
        object value, bool appendAsJson = false, SchemaDataFormat stated = SchemaDataFormat.Unspecified
    ) {
        var type     = value.GetType();
        var resolver = MessageResolver.From(new MessageCatalog("classify-resolution").Map(type.Name, type));
        var sut      = new ClassifyWriteStep(resolver, appendAsJson);

        var record = AppendRecord.Create(value, r => r with {
            Stream     = "classify-resolution",
            DataFormat = stated
        });

        var resolution = await sut.Execute(record, CancellationToken.None);

        return resolution.Match(
            named   => named.Format,
            encoded => encoded.Schema.DataFormat);
    }

    #region ->> Unstated: the type decides <<-

    [Test]
    public async ValueTask takes_json_for_a_plain_type() {
        // Act
        var format = await Resolve(new Payload("x"));

        // Assert
        await Assert.That(format).IsEqualTo(SchemaDataFormat.Json);
    }

    [Test]
    public async ValueTask takes_protobuf_for_a_protobuf_message() {
        // Act
        var format = await Resolve(new Protocol.Empty());

        // Assert
        await Assert.That(format).IsEqualTo(SchemaDataFormat.Protobuf);
    }

    [Test]
    public async ValueTask takes_avro_for_an_avro_registered_type() {
        // Arrange
        AvroCodecRegistry.Register(
            typeof(AvroPayload), static (_, _) => { }, static _ => new AvroPayload("x"));

        // Act
        var format = await Resolve(new AvroPayload("x"));

        // Assert
        await Assert.That(format).IsEqualTo(SchemaDataFormat.Avro);
    }

    #endregion // Unstated: the type decides

    #region ->> AppendAsJson <<-

    [Test]
    public async ValueTask append_as_json_overrides_the_type() {
        // Arrange — a Protobuf message, so the type's own format would be Protobuf.

        // Act
        var format = await Resolve(new Protocol.Empty(), appendAsJson: true);

        // Assert
        await Assert.That(format).IsEqualTo(SchemaDataFormat.Json);
    }

    // Every stated format, against the override that would otherwise force Json.
    public sealed class StatedFormats : TestCaseDataGenerator<SchemaDataFormat> {
        protected override IEnumerable<TestCase<SchemaDataFormat>> Data() {
            yield return new(SchemaDataFormat.Json, DisplayName: "json");
            yield return new(SchemaDataFormat.Protobuf, DisplayName: "protobuf");
            yield return new(SchemaDataFormat.Avro, DisplayName: "avro");
        }
    }

    [Test]
    [StatedFormats]
    public async ValueTask a_stated_format_beats_append_as_json(SchemaDataFormat stated) {
        // Act
        var format = await Resolve(new Payload("x"), appendAsJson: true, stated: stated);

        // Assert
        await Assert.That(format).IsEqualTo(stated);
    }

    #endregion // AppendAsJson

    #endregion // Format resolution
}
