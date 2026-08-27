using Kurrent.Surge.Codecs;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Tests.Codecs;

[Category("Codecs")]
public class SchemaCodecProviderTests {
	[Test]
	public async ValueTask supports_and_resolves_registered_formats() {
		// Arrange
		var json     = new FakeSchemaCodec(SchemaDataFormat.Json);
		var protobuf = new FakeSchemaCodec(SchemaDataFormat.Protobuf);

		// Act
		var sut = new SchemaCodecProvider([json, protobuf]);

		// Assert
		await Assert.That(sut.SupportsDataFormat(SchemaDataFormat.Json)).IsTrue();
		await Assert.That(sut.SupportsDataFormat(SchemaDataFormat.Protobuf)).IsTrue();
		await Assert.That(sut.SupportsDataFormat(SchemaDataFormat.Avro)).IsFalse();
		await Assert.That(sut.GetCodec(SchemaDataFormat.Json)).IsEqualTo(json);
	}

	[Test]
	public async ValueTask unsupported_format_throws() {
		// Arrange
		var sut = new SchemaCodecProvider([new FakeSchemaCodec(SchemaDataFormat.Json)]);

		// Act & Assert
		await Assert.That(() => sut.GetCodec(SchemaDataFormat.Avro))
			.Throws<SchemaCodecNotFoundException>();
	}

	[Test]
	public async ValueTask duplicate_data_format_throws() {
		// Arrange
		var first  = new FakeSchemaCodec(SchemaDataFormat.Avro);
		var second = new FakeSchemaCodec(SchemaDataFormat.Avro);

		// Act & Assert
		await Assert.That(() => new SchemaCodecProvider([first, second]))
			.Throws<DuplicateSchemaCodecException>();
	}

	sealed class FakeSchemaCodec(SchemaDataFormat dataFormat) : ISchemaCodec {
		public SchemaDataFormat DataFormat { get; } = dataFormat;

		public ReadOnlyMemory<byte> Encode(object? value) => ReadOnlyMemory<byte>.Empty;

		public object Decode(ReadOnlyMemory<byte> data, Type resolvedType) => data;
	}
}
