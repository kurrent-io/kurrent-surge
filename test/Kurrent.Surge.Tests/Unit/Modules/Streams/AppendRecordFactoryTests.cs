using Kurrent.Surge.Client.Streams;
using Kurrent.Schema;

namespace Kurrent.Surge.Tests.Modules.Streams;

public class AppendRecordFactoryTests {
    [Test]
    public async ValueTask create_applies_defaults() {
        // Act
        var record = AppendRecord.Create("payload");

        // Assert
        await Assert.That(record.Value).IsEqualTo("payload");
        await Assert.That(record.DataFormat).IsEqualTo(SchemaDataFormat.Json);
        await Assert.That(record.RecordId).IsNotEqualTo(Guid.Empty);
        await Assert.That(record.Stream).IsEqualTo(StreamName.Unset);
    }

    [Test]
    public async ValueTask create_with_configure_sets_all_fields() {
        // Arrange
        var recordId = Guid.NewGuid();
        var metadata = new Metadata().With("key", "value");

        // Act
        var record = AppendRecord.Create("payload", r => {
            r.Stream     = "my-stream";
            r.DataFormat = SchemaDataFormat.Json;
            r.RecordId   = recordId;
            r.Metadata   = metadata;
        });

        // Assert
        await Assert.That(record.Stream).IsEqualTo((StreamName)"my-stream");
        await Assert.That(record.Value).IsEqualTo("payload");
        await Assert.That(record.DataFormat).IsEqualTo(SchemaDataFormat.Json);
        await Assert.That(record.RecordId).IsEqualTo(recordId);
        await Assert.That(record.Metadata).IsSameReferenceAs(metadata);
    }

    [Test]
    public async ValueTask create_configure_merges_metadata_in_place() {
        // Act
        var record = AppendRecord.Create("payload", r => {
            r.Stream = "test-stream";
            r.Metadata.With("existing", "one")
                      .With("added", "two");
        });

        // Assert
        await Assert.That(record.Metadata["existing"]).IsEqualTo("one");
        await Assert.That(record.Metadata["added"]).IsEqualTo("two");
    }

    [Test]
    public async ValueTask create_assigns_a_distinct_record_id_per_call() {
        // Act
        var first  = AppendRecord.Create("payload");
        var second = AppendRecord.Create("payload");

        // Assert
        await Assert.That(first.RecordId).IsNotEqualTo(second.RecordId);
    }
}
