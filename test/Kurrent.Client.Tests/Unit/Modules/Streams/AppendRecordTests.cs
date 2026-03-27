using Kurrent.Client.Streams;
using Kurrent.Validation;

namespace Kurrent.Client.Tests.Modules.Streams;

public class AppendRecordTests {
    [Test]
    public void ensure_valid_passes_for_valid_record() {
        var record = AppendRecord.New
            .WithValue("test-payload")
            .ForStream("test-stream")
            .Build();

        record.EnsureValid();
    }

    [Test]
    public async ValueTask ensure_valid_returns_self_for_fluent_chaining() {
        var record = AppendRecord.New
            .WithValue("test-payload")
            .ForStream("test-stream")
            .Build();

        var result = record.EnsureValid();

        await Assert.That(result).IsSameReferenceAs(record);
    }

    [Test]
    public async ValueTask ensure_valid_throws_when_record_id_is_empty() {
        var record = new AppendRecord {
            RecordId   = Guid.Empty,
            Value      = "test-payload",
            DataFormat = SchemaDataFormat.Json,
            Stream     = "test-stream"
        };

        var ex = Assert.Throws<ValidationException>(() => record.EnsureValid());

        await Assert.That(ex.Errors).Count().IsEqualTo(1);
        await Assert.That(ex.Errors[0].FieldName).IsEqualTo(nameof(AppendRecord.RecordId));
    }

    [Test]
    public async ValueTask ensure_valid_throws_when_value_is_null() {
        var record = new AppendRecord {
            Value      = null!,
            DataFormat = SchemaDataFormat.Json,
            Stream     = "test-stream"
        };

        var ex = Assert.Throws<ValidationException>(() => record.EnsureValid());

        await Assert.That(ex.Errors).Contains(p => p.FieldName == nameof(AppendRecord.Value));
    }

    [Test]
    public async ValueTask ensure_valid_throws_when_data_format_is_unspecified() {
        var record = new AppendRecord {
            Value      = "test-payload",
            DataFormat = SchemaDataFormat.Unspecified,
            Stream     = "test-stream"
        };

        var ex = Assert.Throws<ValidationException>(() => record.EnsureValid());

        await Assert.That(ex.Errors).Contains(p => p.FieldName == nameof(AppendRecord.DataFormat));
    }

    [Test]
    public async ValueTask ensure_valid_throws_when_byte_value_has_non_bytes_format() {
        var record = new AppendRecord {
            Value      = new byte[] { 1, 2, 3 },
            DataFormat = SchemaDataFormat.Json,
            Stream     = "test-stream"
        };

        var ex = Assert.Throws<ValidationException>(() => record.EnsureValid());

        await Assert.That(ex.Errors).Contains(p => p.FieldName == nameof(AppendRecord.DataFormat));
    }

    [Test]
    public void ensure_valid_passes_when_byte_value_has_bytes_format() {
        var record = new AppendRecord {
            Value      = new byte[] { 1, 2, 3 },
            DataFormat = SchemaDataFormat.Bytes,
            Stream     = "test-stream"
        };

        record.EnsureValid();
    }

    [Test]
    public async ValueTask ensure_valid_collects_all_problems_at_once() {
        var record = new AppendRecord {
            RecordId   = Guid.Empty,
            Value      = null!,
            DataFormat = SchemaDataFormat.Unspecified,
            Stream     = StreamName.Unset
        };

        var ex = Assert.Throws<ValidationException>(() => record.EnsureValid());

        await Assert.That(ex.Errors).Count().IsEqualTo(3);
        await Assert.That(ex.Errors).Contains(p => p.FieldName == nameof(AppendRecord.RecordId));
        await Assert.That(ex.Errors).Contains(p => p.FieldName == nameof(AppendRecord.Value));
        await Assert.That(ex.Errors).Contains(p => p.FieldName == nameof(AppendRecord.DataFormat));
    }
}
