using System.Buffers;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Schema.Serialization.Bytes;
using Kurrent.Surge.Client.Streams;
using Kurrent.Schema;

namespace Kurrent.Surge.Tests.Schema.Serialization.Bytes;

[InheritsTests]
[Category("SerDe")]
public class BytesSerializerTests : SchemaSerializerTestFixture {
	protected override SchemaDataFormat ExpectedDataFormat => SchemaDataFormat.Bytes;

	protected override ISchemaSerializer CreateSut() => new BytesPassthroughSerializer();

	protected override object[] CreateTestMessages() => [
		Faker.Random.Bytes(16),
		(ReadOnlyMemory<byte>)Faker.Random.Bytes(32),
		(Memory<byte>)Faker.Random.Bytes(64),
		new ReadOnlySequence<byte>(Faker.Random.Bytes(128)),
		new ArraySegment<byte>(Faker.Random.Bytes(256))
	];

	protected override SchemaSerializationContext CreateSerializeContext() => new() {
		Stream       = "test-stream",
		RecordSchema = new RecordSchemaInfo { DataFormat = SchemaDataFormat.Bytes }
	};

	protected override async ValueTask AssertDeserialized(object original, object? deserialized) {
		await Assert.That(deserialized).IsNotNull();
		await Assert.That(deserialized).IsTypeOf<ReadOnlyMemory<byte>>();

		var expected = original switch {
			byte[] bytes               => bytes,
			ReadOnlyMemory<byte> rom   => rom.ToArray(),
			Memory<byte> mem           => mem.ToArray(),
			ReadOnlySequence<byte> seq => seq.ToArray(),
			ArraySegment<byte> seg     => seg.ToArray(),
			_                          => throw new InvalidOperationException($"Unexpected type: {original.GetType()}")
		};

		var actual = (ReadOnlyMemory<byte>)deserialized!;

		await Assert.That(actual.Span.ToArray()).IsEquivalentTo(expected);
	}

	[Test]
	public async ValueTask serialize_byte_array_returns_same_bytes() {
		// Arrange
		var context = CreateSerializeContext();
		var input   = Faker.Random.Bytes(16);

		// Act
		var result = await Sut.Serialize(input, context).ConfigureAwait(false);

		// Assert
		await Assert.That(result.Span.ToArray()).IsEquivalentTo(input);
	}

	[Test]
	public async ValueTask serialize_readonly_memory_returns_same_bytes() {
		// Arrange
		var context = CreateSerializeContext();
		ReadOnlyMemory<byte> input = Faker.Random.Bytes(16);

		// Act
		var result = await Sut.Serialize(input, context).ConfigureAwait(false);

		// Assert
		await Assert.That(result.Span.ToArray()).IsEquivalentTo(input.ToArray());
	}

	[Test]
	public async ValueTask serialize_memory_returns_same_bytes() {
		// Arrange
		var context = CreateSerializeContext();
		Memory<byte> input = Faker.Random.Bytes(16);

		// Act
		var result = await Sut.Serialize(input, context).ConfigureAwait(false);

		// Assert
		await Assert.That(result.Span.ToArray()).IsEquivalentTo(input.ToArray());
	}

	[Test]
	public async ValueTask serialize_readonly_sequence_returns_same_bytes() {
		// Arrange
		var context = CreateSerializeContext();
		var bytes   = Faker.Random.Bytes(16);
		var input   = new ReadOnlySequence<byte>(bytes);

		// Act
		var result = await Sut.Serialize(input, context).ConfigureAwait(false);

		// Assert
		await Assert.That(result.Span.ToArray()).IsEquivalentTo(bytes);
	}

	[Test]
	public async ValueTask serialize_array_segment_returns_same_bytes() {
		// Arrange
		var context = CreateSerializeContext();
		var bytes   = Faker.Random.Bytes(16);
		var input   = new ArraySegment<byte>(bytes);

		// Act
		var result = await Sut.Serialize(input, context).ConfigureAwait(false);

		// Assert
		await Assert.That(result.Span.ToArray()).IsEquivalentTo(bytes);
	}

	[Test]
	public async ValueTask deserialize_returns_readonly_memory_of_input() {
		// Arrange
		var context = CreateSerializeContext();
		ReadOnlyMemory<byte> input = Faker.Random.Bytes(16);

		// Act
		var result = await Sut.Deserialize(input, context).ConfigureAwait(false);

		// Assert
		await Assert.That(result).IsTypeOf<DeserializationResult.Deserialized>();
		var value = ((DeserializationResult.Deserialized)result).Value;
		await Assert.That(value).IsTypeOf<ReadOnlyMemory<byte>>();
		var memory = (ReadOnlyMemory<byte>)value;
		await Assert.That(memory.Span.ToArray()).IsEquivalentTo(input.ToArray());
	}

	[Test]
	public async ValueTask serialize_non_byte_type_throws() {
		// Arrange
		var context = CreateSerializeContext();

		// Act & Assert
		await Assert.That(async () => await Sut.Serialize("not-bytes", context).ConfigureAwait(false))
			.ThrowsException();
	}
}
