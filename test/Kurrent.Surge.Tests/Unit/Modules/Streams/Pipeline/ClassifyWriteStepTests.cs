using System.Buffers;
using Bogus;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Tests.Modules.Streams.Pipeline;

// Bytes records skip the codec entirely and unwrap through this step — kept in sync with the
// shapes BytesPassthroughSerializer used to accept before the governed pipeline replaced it.
[Category("Streams")]
public class ClassifyWriteStepTests {
    static readonly Faker Faker = new();

    static async ValueTask<ReadOnlyMemory<byte>> Classify(object value) {
        var sut = new ClassifyWriteStep(MessageResolver.From());

        var record = AppendRecord.Create(value, r => {
            r.Stream     = "classify-write-step-tests";
            r.DataFormat = SchemaDataFormat.Bytes;
            r.SchemaName = "classify-write-step-schema";
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
}
