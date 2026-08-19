using Bogus;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Codecs.Json;
using Kurrent.Surge.Codecs.Protobuf;
using Kurrent.Testkit.Datasets.ArcadeRacer;

namespace Kurrent.Surge.Tests.Codecs;

[Category("Codecs")]
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
