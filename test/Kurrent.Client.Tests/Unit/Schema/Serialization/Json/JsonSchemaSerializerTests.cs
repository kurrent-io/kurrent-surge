using System.Text;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Schema.Serialization.Json;
using Kurrent.Client.Streams;
using Kurrent.Testkit.Datasets.ArcadeRacer;

namespace Kurrent.Client.Tests.Schema.Serialization.Json;

[InheritsTests]
[Category("SerDe")]
public class JsonSchemaSerializerTests : SchemaSerializerTestFixture {
	static readonly ArcadeRacerDataSet ArcadeRacer = ArcadeRacerDataSet.Instance;

	protected override SchemaDataFormat ExpectedDataFormat => SchemaDataFormat.Json;

	protected override ISchemaSerializer CreateSut() {
		var typeMapper    = new MessageTypeMapper();
		var schemaManager = new SchemaManager(null!, null!, typeMapper);

		return new JsonSchemaSerializer(
			new JsonSchemaSerializerOptions { SchemaRegistration = SchemaRegistrationOptions.AutoMap },
			schemaManager
		);
	}

	protected override object[] CreateTestMessages() => [
		ArcadeRacer.RaceEvents.LapCompleted.Generate(),
		ArcadeRacer.RaceEvents.OvertakePerformed.Generate(),
		ArcadeRacer.RaceEvents.PitStopPerformed.Generate()
	];

	protected override SchemaSerializationContext CreateSerializeContext() => new() {
		Stream       = "test-stream",
		RecordSchema = new RecordSchemaInfo {
			SchemaName   = "test-schema",
			SchemaFormat = SchemaDataFormat.Json
		}
	};

	protected override async ValueTask AssertDeserialized(object original, object? deserialized) {
		await Assert.That(deserialized).IsNotNull();

		var originalJson     = System.Text.Json.JsonSerializer.Serialize(original, original.GetType());
		var deserializedJson = System.Text.Json.JsonSerializer.Serialize(deserialized!, deserialized!.GetType());

		await Assert.That(deserializedJson).IsEqualTo(originalJson);
	}

	[Test]
	public async ValueTask serialize_uses_camel_case_by_default() {
		// Arrange
		var context = CreateSerializeContext();
		var lap     = ArcadeRacer.RaceEvents.LapCompleted.Generate();

		// Act
		var result = await Sut.Serialize(lap, context).ConfigureAwait(false);
		var json   = Encoding.UTF8.GetString(result.Span);

		// Assert
		await Assert.That(json).Contains("\"eventId\"");
		await Assert.That(json).Contains("\"carId\"");
		await Assert.That(json).Contains("\"lapTime\"");
		await Assert.That(json).Contains("\"cumulativeTime\"");

		await Assert.That(json).DoesNotContain("\"EventId\"");
		await Assert.That(json).DoesNotContain("\"CarId\"");
	}

	[Test]
	public async ValueTask deserialize_malformed_bytes_throws() {
		// Arrange - serialize first to register the schema via auto-map
		var context = CreateSerializeContext();
		var lap     = ArcadeRacer.RaceEvents.LapCompleted.Generate();
		await Sut.Serialize(lap, context).ConfigureAwait(false);

		var deserializeContext = CreateDeserializeContext(context);
		var malformedBytes    = new ReadOnlyMemory<byte>([0xFF, 0xFE, 0x00, 0x01]);

		// Act & Assert
		await Assert.That(async () => await Sut.Deserialize(malformedBytes, deserializeContext).ConfigureAwait(false))
			.ThrowsException();
	}
}
