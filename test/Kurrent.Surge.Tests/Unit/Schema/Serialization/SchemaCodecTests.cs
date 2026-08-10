using Bogus;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Schema.Serialization.Json;
using Kurrent.Surge.Client.Schema.Serialization.Protobuf;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Surge.Client.Streams;
using Kurrent.Testkit.Datasets.ArcadeRacer;

namespace Kurrent.Surge.Tests.Schema.Serialization;

[Category("SerDe")]
public class SchemaCodecTests {
	[ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
	public required BogusFaker Faker { get; [UsedImplicitly] init; }

	static readonly ArcadeRacerDataSet ArcadeRacer = ArcadeRacerDataSet.Instance;

	[Test]
	public async ValueTask json_codec_round_trips_message() {
		// Arrange
		var sut          = new JsonSchemaCodec();
		var lap          = ArcadeRacer.RaceEvents.LapCompleted.Generate();
		var expectedJson = System.Text.Json.JsonSerializer.Serialize(lap, lap.GetType());

		// Act
		var encoded = sut.Encode(lap);
		var decoded = sut.Decode(encoded, lap.GetType());

		// Assert
		var decodedJson = System.Text.Json.JsonSerializer.Serialize(decoded, decoded.GetType());
		await Assert.That(decodedJson).IsEqualTo(expectedJson);
	}

	[Test]
	public async ValueTask json_codec_encodes_identically_to_the_serializer_sync_core() {
		// Arrange
		var catalog = new MessageCatalog("codec-tests")
			.Map<Kurrent.Testkit.Datasets.ArcadeRacer.Contracts.LapCompleted>();

		var serializer = (ISchemaSerializer)new JsonSchemaSerializer(
			new JsonSchemaSerializerOptions { SchemaRegistration = SchemaRegistrationOptions.ManualMap },
			new SchemaManager(null!, null!, MessageResolver.From(catalog))
		);

		var context = new SchemaSerializationContext {
			Stream       = "test-stream",
			RecordSchema = new RecordSchemaInfo {
				Schema     = "test-schema",
				DataFormat = SchemaDataFormat.Json
			}
		};

		var sut      = new JsonSchemaCodec();
		var lap      = ArcadeRacer.RaceEvents.LapCompleted.Generate();
		var expected = await serializer.Serialize(lap, context).ConfigureAwait(false);

		// Act
		var encoded = sut.Encode(lap);

		// Assert
		await Assert.That(Convert.ToHexString(encoded.Span)).IsEqualTo(Convert.ToHexString(expected.Span));
	}

	[Test]
	public async ValueTask protobuf_codec_round_trips_message() {
		// Arrange
		var sut      = new ProtobufSchemaCodec();
		var expected = Timestamp.FromDateTimeOffset(Faker.Date.RecentOffset());

		// Act
		var encoded = sut.Encode(expected);
		var decoded = sut.Decode(encoded, typeof(Timestamp));

		// Assert
		await Assert.That(decoded).IsEqualTo(expected);
	}
}
