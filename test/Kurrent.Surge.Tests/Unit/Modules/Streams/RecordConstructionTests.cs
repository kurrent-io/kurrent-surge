using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Codecs.Json;
using Kurrent.Surge.Schema;
using Kurrent.Testkit.Datasets.ArcadeRacer;

namespace Kurrent.Surge.Tests.Streams;

[Category("Streams")]
public class RecordConstructionTests {
	static readonly ArcadeRacerDataSet ArcadeRacer = ArcadeRacerDataSet.Instance;

	[Test]
	public async ValueTask takes_json_format_from_the_message_type() {
		// Arrange
		var lap = ArcadeRacer.RaceEvents.LapCompleted.Generate();

		// Act
		var record = new Record(lap);

		// Assert
		await Assert.That(record.Schema.DataFormat).IsEqualTo(SchemaDataFormat.Json);
	}

	[Test]
	public async ValueTask takes_protobuf_format_from_the_message_type() {
		// Arrange
		var timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

		// Act
		var record = new Record(timestamp);

		// Assert
		await Assert.That(record.Schema.DataFormat).IsEqualTo(SchemaDataFormat.Protobuf);
	}

	[Test]
	public async ValueTask takes_the_schema_name_from_the_type_name() {
		// Arrange
		var lap          = ArcadeRacer.RaceEvents.LapCompleted.Generate();
		var expectedName = SchemaName.From(lap.GetType().Name);

		// Act
		var record = new Record(lap);

		// Assert
		await Assert.That(record.Schema.Schema).IsEqualTo(expectedName);
	}

	[Test]
	public async ValueTask uses_the_stated_schema_name() {
		// Arrange
		var lap          = ArcadeRacer.RaceEvents.LapCompleted.Generate();
		var expectedName = SchemaName.From("arcade.lap-completed.v1");

		// Act
		var record = new Record(lap, expectedName);

		// Assert
		await Assert.That(record.Schema.Schema).IsEqualTo(expectedName);
	}

	[Test]
	public async ValueTask uses_the_stated_data_format() {
		// Arrange
		var timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

		// Act
		var record = new Record(timestamp, dataFormat: SchemaDataFormat.Json);

		// Assert
		await Assert.That(record.Schema.DataFormat).IsEqualTo(SchemaDataFormat.Json);
	}

	[Test]
	public async ValueTask keeps_every_other_field_of_the_stated_schema() {
		// Arrange
		var lap               = ArcadeRacer.RaceEvents.LapCompleted.Generate();
		var expectedVersionId = SchemaVersionId.From(Guid.NewGuid());
		var expectedName      = SchemaName.From(lap.GetType().Name);

		var expectedSchema = new RecordSchemaInfo {
			Schema     = expectedName,
			DataFormat = SchemaDataFormat.Json,
			VersionId  = expectedVersionId
		};

		// Act
		var record = new Record(lap, new RecordSchemaInfo { VersionId = expectedVersionId });

		// Assert
		await Assert.That(record.Schema).IsEqualTo(expectedSchema);
	}

	[Test]
	public async ValueTask carries_the_value_as_decoded() {
		// Arrange
		var lap = ArcadeRacer.RaceEvents.LapCompleted.Generate();

		// Act
		var record = new Record(lap);

		// Assert
		await Assert.That(record.Value).IsEqualTo(lap);
		await Assert.That(record.ValueType).IsEqualTo(lap.GetType());
		await Assert.That(record.IsDecoded).IsTrue();
	}

	[Test]
	public async ValueTask encodes_data_that_decodes_back_to_the_value() {
		// Arrange
		var lap          = ArcadeRacer.RaceEvents.LapCompleted.Generate();
		var codec        = new JsonSchemaCodec();
		var expectedJson = System.Text.Json.JsonSerializer.Serialize(lap, lap.GetType());

		// Act
		var record  = new Record(lap);
		var decoded = codec.Decode(record.Data, lap.GetType());

		// Assert
		var decodedJson = System.Text.Json.JsonSerializer.Serialize(decoded, decoded.GetType());
		await Assert.That(decodedJson).IsEqualTo(expectedJson);
	}

	[Test]
	public async ValueTask keeps_the_fields_set_by_the_object_initializer() {
		// Arrange
		var lap              = ArcadeRacer.RaceEvents.LapCompleted.Generate();
		var expectedPosition = new RecordPosition {
			LogPosition    = LogPosition.From(42),
			StreamPosition = new StreamPosition(StreamName.From("race-1"), Revision.From(7))
		};

		// Act
		var record = new Record(lap) { Position = expectedPosition };

		// Assert
		await Assert.That(record.Position).IsEqualTo(expectedPosition);
		await Assert.That(record.IsDecoded).IsTrue();
	}

	[Test]
	public async ValueTask rejects_the_bytes_format() {
		// Arrange
		var lap = ArcadeRacer.RaceEvents.LapCompleted.Generate();

		// Act
		var act = () => new Record(lap, dataFormat: SchemaDataFormat.Bytes);

		// Assert
		await Assert.That(act).Throws<ArgumentException>();
	}
}
