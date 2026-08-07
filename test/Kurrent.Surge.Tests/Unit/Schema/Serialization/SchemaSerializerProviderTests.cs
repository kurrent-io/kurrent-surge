using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Tests.Schema.Serialization;

[Category("SerDe")]
public class SchemaSerializerProviderTests {
	[Test]
	public async ValueTask supports_and_resolves_registered_formats() {
		// Arrange
		var json     = new FakeSchemaSerializer(SchemaDataFormat.Json);
		var protobuf = new FakeSchemaSerializer(SchemaDataFormat.Protobuf);

		// Act
		var sut = new SchemaSerializerProvider([json, protobuf]);

		// Assert
		await Assert.That(sut.SupportsDataFormat(SchemaDataFormat.Json)).IsTrue();
		await Assert.That(sut.SupportsDataFormat(SchemaDataFormat.Protobuf)).IsTrue();
		await Assert.That(sut.SupportsDataFormat(SchemaDataFormat.MessagePack)).IsFalse();
		await Assert.That(sut.GetSerializer(SchemaDataFormat.Json)).IsEqualTo(json);
	}

	[Test]
	public async ValueTask unsupported_format_throws() {
		// Arrange
		var sut = new SchemaSerializerProvider([new FakeSchemaSerializer(SchemaDataFormat.Json)]);

		// Act & Assert
		await Assert.That(() => sut.GetSerializer(SchemaDataFormat.MessagePack))
			.Throws<SchemaSerializerNotFoundException>();
	}

	[Test]
	public async ValueTask duplicate_data_format_throws() {
		// Arrange
		var first  = new FakeSchemaSerializer(SchemaDataFormat.MessagePack);
		var second = new FakeSchemaSerializer(SchemaDataFormat.MessagePack);

		// Act & Assert
		await Assert.That(() => new SchemaSerializerProvider([first, second]))
			.Throws<DuplicateSchemaSerializerException>();
	}

	sealed class FakeSchemaSerializer(SchemaDataFormat dataFormat) : ISchemaSerializer {
		public SchemaDataFormat DataFormat { get; } = dataFormat;

		public ValueTask<ReadOnlyMemory<byte>> Serialize(object? value, SchemaSerializationContext context) =>
			ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);

		public ValueTask<DeserializationResult> Deserialize(ReadOnlyMemory<byte> data, SchemaSerializationContext context) =>
			ValueTask.FromResult<DeserializationResult>(DeserializationResult.NoData.Instance);
	}
}
