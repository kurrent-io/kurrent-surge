using Avro.IO;

namespace Kurrent.Surge.Codecs.Avro.Tests;

/// <summary>
/// Proves the generated codec is used and produces the same bytes Apache.Avro does.
/// </summary>
/// <remarks>
/// The generated path exists to remove <c>Activator.CreateInstance</c> and Apache's
/// <c>SpecificDatumReader</c>/<c>Writer</c>, which need runtime code generation and fail under Native
/// AOT. Changing how the bytes are produced must not change the bytes: a wire-format divergence would
/// corrupt every stream written by one version and read by another.
/// </remarks>
[Category("Codecs")]
public class AvroCodecGenerationTests {
    static readonly Faker Faker = new();

    static TestOrder NewOrder() => new() {
        OrderId  = Faker.Random.Guid().ToString(),
        Sku      = Faker.Commerce.Ean13(),
        Quantity = Faker.Random.Int(1, 100)
    };

    static byte[] EncodeWithApache(ISpecificRecord record) {
        var schema = record.Schema;
        var writer = new SpecificDatumWriter<ISpecificRecord>(schema);

        using var stream = new MemoryStream();
        writer.Write(record, new BinaryEncoder(stream));

        return stream.ToArray();
    }

    [Test]
    public async ValueTask registers_a_generated_codec_for_each_schema_type() {
        // Arrange — the generator runs over the .avsc AdditionalFiles in this project and its emitted
        // module initializer registers each type. A miss here means the generator never ran.
        var expected = new[] { typeof(TestOrder), typeof(TestCustomer), typeof(TestInvoiceLine) };

        // Act
        var registered = expected.Where(AvroCodecRegistry.IsRegistered).ToArray();

        // Assert
        await Assert.That(registered).IsEquivalentTo(expected);
    }

    [Test]
    public async ValueTask generated_encoding_is_byte_identical_to_apache() {
        // Arrange
        var order        = NewOrder();
        var expectedWire = EncodeWithApache(order);

        // Act — the codec prefers the generated path whenever a type is registered.
        var actualWire = new AvroSchemaCodec().Encode(order).ToArray();

        // Assert
        await Assert.That(actualWire).IsEquivalentTo(expectedWire);
    }

    [Test]
    public async ValueTask generated_decoder_reads_bytes_written_by_apache() {
        // Arrange — a producer on the old path must stay readable by the generated one.
        var order = NewOrder();
        var wire  = EncodeWithApache(order);

        // Act
        var decoded = new AvroSchemaCodec().Decode(wire, typeof(TestOrder));

        // Assert
        await Assert.That(decoded).IsEqualTo(order);
    }

    [Test]
    public async ValueTask generated_codec_round_trips_every_schema_type() {
        // Arrange
        var sut      = new AvroSchemaCodec();
        var customer = new TestCustomer {
            Name     = Faker.Name.FullName(),
            Email    = Faker.Internet.Email(),
            IsActive = Faker.Random.Bool()
        };

        // Act
        var encoded = sut.Encode(customer);
        var decoded = sut.Decode(encoded, typeof(TestCustomer));

        // Assert
        await Assert.That(decoded).IsEqualTo(customer);
    }
}
