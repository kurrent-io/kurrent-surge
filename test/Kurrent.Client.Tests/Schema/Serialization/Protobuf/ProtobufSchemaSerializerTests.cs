using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Schema.Serialization.Protobuf;
using Kurrent.Client.Streams;
using KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Client.Tests.Schema.Serialization.Protobuf;

[InheritsTests]
[Category("SerDe")]
public class ProtobufSchemaSerializerTests : SchemaSerializerTestFixture {
	MessageTypeMapper _typeMapper = null!;

	protected override SchemaDataFormat ExpectedDataFormat => SchemaDataFormat.Protobuf;

	protected override ISchemaSerializer CreateSut() {
		_typeMapper = new MessageTypeMapper();
		var schemaManager = new SchemaManager(null!, null!, _typeMapper);

		return new ProtobufSchemaSerializer(
			new ProtobufSchemaSerializerOptions { SchemaRegistration = SchemaRegistrationOptions.AutoMap },
			schemaManager
		);
	}

	protected override object[] CreateTestMessages() => [
		new AppendResponse {
			Stream         = Faker.Random.AlphaNumeric(12),
			StreamRevision = Faker.Random.Long(0, 1_000_000),
			Position       = Faker.Random.Long(0, 10_000_000)
		},
		new KurrentDB.Protocol.V2.Streams.StreamRevision {
			Stream   = Faker.Random.AlphaNumeric(12),
			Revision = Faker.Random.Long(0, 1_000_000)
		},
		new Query {
			Expression = Faker.Lorem.Sentence()
		}
	];

	protected override SchemaSerializationContext CreateSerializeContext() => new() {
		Stream       = "test-stream",
		RecordSchema = new RecordSchemaInfo {
			SchemaName   = "test-schema",
			SchemaFormat = SchemaDataFormat.Protobuf
		}
	};

	protected override void OnTearDown() => ProtobufMessages.System.ClearCache();

	SchemaSerializationContext CreateDeserializeContext(string schemaName) => new() {
		Stream       = "test-stream",
		RecordSchema = new RecordSchemaInfo {
			SchemaName   = schemaName,
			SchemaFormat = SchemaDataFormat.Protobuf
		}
	};

	[Test]
	public async ValueTask serialize_non_protobuf_throws() {
		var context = CreateSerializeContext();

		await Assert.That(async () => await _sut.Serialize("hello", context).ConfigureAwait(false))
			.ThrowsException();
	}

	[Test]
	public async ValueTask deserialize_with_non_protobuf_type_throws() {
		_typeMapper.TryMap<string>("bad-schema");

		var context = CreateDeserializeContext("bad-schema");
		var data    = new ReadOnlyMemory<byte>([1, 2, 3]);

		await Assert.That(async () => await _sut.Deserialize(data, context).ConfigureAwait(false))
			.ThrowsException();
	}
}
