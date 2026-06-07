using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Schema.Serialization.Protobuf;
using Kurrent.Client.Streams;

using StreamRevision = KurrentDB.Protocol.V2.Streams.StreamRevision;

namespace Kurrent.Client.Tests.Schema.Serialization.Protobuf;

[InheritsTests]
[Category("SerDe")]
public class ProtobufSchemaSerializerTests : SchemaSerializerTestFixture {
	protected override SchemaDataFormat ExpectedDataFormat => SchemaDataFormat.Protobuf;

	protected override ISchemaSerializer CreateSut() {
		// No auto-mapping: every type the fixture serializes must be in the catalog.
		var catalog = new MessageCatalog("protobuf-tests")
			.Map<KurrentDB.Protocol.V2.Streams.AppendRecordsResponse>()
			.Map<KurrentDB.Protocol.V2.Streams.StreamRevision>()
			.Map<KurrentDB.Protocol.V2.Streams.ConsistencyCheck.Types.QueryPredicateCheck>()
			.Map<string>("bad-schema"); // for deserialize_with_non_protobuf_type_throws

		var schemaManager = new SchemaManager(null!, null!, catalog.GetResolver());

		return new ProtobufSchemaSerializer(
			new ProtobufSchemaSerializerOptions { SchemaRegistration = SchemaRegistrationOptions.ManualMap },
			schemaManager
		);
	}

	protected override object[] CreateTestMessages() => [
		new KurrentDB.Protocol.V2.Streams.AppendRecordsResponse {
			Position  = Faker.Random.Long(0, 10_000_000),
            Revisions = {
                new StreamRevision {
                    Stream   = Faker.Random.AlphaNumeric(12),
                    Revision = Faker.Random.Long(0, 1_000_000)
                },
                new StreamRevision {
                    Stream   = Faker.Random.AlphaNumeric(12),
                    Revision = Faker.Random.Long(0, 1_000_000)
                }
            }
		},
		new KurrentDB.Protocol.V2.Streams.StreamRevision {
			Stream   = Faker.Random.AlphaNumeric(12),
			Revision = Faker.Random.Long(0, 1_000_000)
		},
		new KurrentDB.Protocol.V2.Streams.ConsistencyCheck.Types.QueryPredicateCheck {
			Expression = Faker.Lorem.Sentence()
		}
	];

	protected override SchemaSerializationContext CreateSerializeContext() => new() {
		Stream       = "test-stream",
		RecordSchema = new RecordSchemaInfo {
			Schema   = "test-schema",
			DataFormat = SchemaDataFormat.Protobuf
		}
	};

	protected override void OnTearDown() => ProtobufMessages.System.ClearCache();

	SchemaSerializationContext CreateDeserializeContext(string schemaName) => new() {
		Stream       = "test-stream",
		RecordSchema = new RecordSchemaInfo {
			Schema   = schemaName,
			DataFormat = SchemaDataFormat.Protobuf
		}
	};

	[Test]
	public async ValueTask serialize_non_protobuf_throws() {
		// Arrange
		var context = CreateSerializeContext();

		// Act & Assert
		await Assert.That(async () => await Sut.Serialize("hello", context).ConfigureAwait(false))
			.ThrowsException();
	}

	[Test]
	public async ValueTask deserialize_with_non_protobuf_type_throws() {
		// Arrange — "bad-schema" → string is mapped in the SUT catalog at construction.
		var context = CreateDeserializeContext("bad-schema");
		var data    = new ReadOnlyMemory<byte>([1, 2, 3]);

		// Act & Assert
		await Assert.That(async () => await Sut.Deserialize(data, context).ConfigureAwait(false))
			.ThrowsException();
	}
}
