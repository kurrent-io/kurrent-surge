using Kurrent.Client.Streams;
using Kurrent.Validation;

namespace Kurrent.Client.Tests.Modules.Streams;

public class AppendRecordBuilderTests {
    [Test]
    public async ValueTask builder_produces_valid_record_with_all_fields() {
        // Arrange
        var recordId = Guid.NewGuid();
        var metadata = new Metadata().With("key", "value");

        // Act
        var record = AppendRecord.New
            .ForStream("my-stream")
            .WithValue("payload")
            .WithDataFormat(SchemaDataFormat.Json)
            .WithId(recordId)
            .WithMetadata(metadata)
            .Build();

        // Assert
        await Assert.That(record.Stream).IsEqualTo((StreamName)"my-stream");
        await Assert.That(record.Value).IsEqualTo("payload");
        await Assert.That(record.DataFormat).IsEqualTo(SchemaDataFormat.Json);
        await Assert.That(record.RecordId).IsEqualTo(recordId);
        await Assert.That(record.Metadata).IsSameReferenceAs(metadata);
    }

    [Test]
    public async ValueTask builder_defaults_to_json_format() {
        // Act
        var record = AppendRecord.New
            .WithValue("payload")
            .ForStream("test-stream")
            .Build();

        // Assert
        await Assert.That(record.DataFormat).IsEqualTo(SchemaDataFormat.Json);
    }

    [Test]
    public async ValueTask builder_defaults_record_id_to_new_guid() {
        // Act
        var record = AppendRecord.New
            .WithValue("payload")
            .ForStream("test-stream")
            .Build();

        // Assert
        await Assert.That(record.RecordId).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async ValueTask from_record_initializes_builder_from_existing_record() {
        // Arrange
        var original = AppendRecord.New
            .ForStream("original-stream")
            .WithValue("original-payload")
            .WithDataFormat(SchemaDataFormat.Json)
            .Build();

        // Act
        var copy = AppendRecord.New
            .FromRecord(original)
            .Build();

        // Assert
        await Assert.That(copy.Stream).IsEqualTo(original.Stream);
        await Assert.That(copy.Value).IsEqualTo(original.Value);
        await Assert.That(copy.DataFormat).IsEqualTo(original.DataFormat);
        await Assert.That(copy.RecordId).IsEqualTo(original.RecordId);
    }

    [Test]
    public async ValueTask with_metadata_key_value_merges_into_existing() {
        // Arrange & Act
        var record = AppendRecord.New
            .ForStream("test-stream")
            .WithValue("payload")
            .WithMetadata(new Metadata().With("existing", "one"))
            .WithMetadata<string>("added", "two")
            .Build();

        // Assert
        await Assert.That(record.Metadata["existing"]).IsEqualTo("one");
        await Assert.That(record.Metadata["added"]).IsEqualTo("two");
    }

    [Test]
    public void build_throws_when_value_is_null() {
        // Arrange
        var builder = AppendRecord.New.ForStream("test-stream");

        // Act & Assert
        Assert.Throws<ValidationException>(() => builder.Build());
    }
}
