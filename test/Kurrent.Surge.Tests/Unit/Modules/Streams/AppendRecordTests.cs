using Kurrent.Toolkit;
using System.Buffers;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Validation;

namespace Kurrent.Surge.Tests.Modules.Streams;

public class AppendRecordTests {
    [Test]
    public void ensure_valid_passes_for_valid_record() {
        var record = AppendRecord.Create("test-payload", r => r.Stream = "test-stream");

        record.EnsureValid();
    }

    [Test]
    public async ValueTask ensure_valid_throws_when_record_id_is_empty() {
        var record = new AppendRecord {
            RecordId   = RecordId.None,
            Value      = "test-payload",
            DataFormat = SchemaDataFormat.Json,
            Stream     = "test-stream"
        };

        var ex = Assert.Throws<ValidationException>(record.EnsureValid);

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

        var ex = Assert.Throws<ValidationException>(record.EnsureValid);

        await Assert.That(ex.Errors).Contains(p => p.FieldName == nameof(AppendRecord.Value));
    }

    [Test]
    public async ValueTask ensure_valid_accepts_an_unstated_data_format() {
        // Arrange — unstated is the normal case: the write pipeline derives the format from the
        // message type, so validation must neither reject it nor settle it.
        var record = new AppendRecord {
            Value      = "test-payload",
            DataFormat = SchemaDataFormat.Unspecified,
            Stream     = "test-stream"
        };

        // Act
        record.EnsureValid();

        // Assert
        await Assert.That(record.DataFormat).IsEqualTo(SchemaDataFormat.Unspecified);
    }

    [Test]
    public async ValueTask ensure_valid_throws_when_byte_value_has_non_bytes_format() {
        var record = new AppendRecord {
            Value      = new byte[] { 1, 2, 3 },
            DataFormat = SchemaDataFormat.Json,
            Stream     = "test-stream"
        };

        var ex = Assert.Throws<ValidationException>(record.EnsureValid);

        await Assert.That(ex.Errors).Contains(p => p.FieldName == nameof(AppendRecord.DataFormat));
    }

    [Test]
    public void ensure_valid_passes_when_byte_value_has_bytes_format() {
        var record = new AppendRecord {
            Value      = new byte[] { 1, 2, 3 },
            DataFormat = SchemaDataFormat.Bytes,
            SchemaName = "test-bytes-schema",
            Stream     = "test-stream"
        };

        record.EnsureValid();
    }

    [Test]
    public async ValueTask ensure_valid_throws_when_bytes_format_has_no_schema_name() {
        var record = new AppendRecord {
            Value      = new byte[] { 1, 2, 3 },
            DataFormat = SchemaDataFormat.Bytes,
            Stream     = "test-stream"
        };

        var ex = Assert.Throws<ValidationException>(record.EnsureValid);

        await Assert.That(ex.Errors).Count().IsEqualTo(1);
        await Assert.That(ex.Errors[0].FieldName).IsEqualTo(nameof(AppendRecord.SchemaName));
    }

    public class BytePayloads : TestCaseDataGenerator<object> {
        protected override IEnumerable<TestCase<object>> Data() {
            var bytes = new byte[] { 1, 2, 3 };

            yield return new(bytes);
            yield return new(new ReadOnlyMemory<byte>(bytes));
            yield return new(new Memory<byte>(bytes));
            yield return new(new ArraySegment<byte>(bytes));
            yield return new(new ReadOnlySequence<byte>(bytes));
        }
    }

    [Test]
    [BytePayloads]
    public async ValueTask create_accepts_every_raw_payload_shape(object payload) {
        // Arrange — each shape reaches a Create overload, four by implicit conversion.
        var record = payload switch {
            byte[] v               => AppendRecord.Create(v, "test-raw-schema"),
            ReadOnlyMemory<byte> v => AppendRecord.Create(v, "test-raw-schema"),
            Memory<byte> v         => AppendRecord.Create(v, "test-raw-schema"),
            ArraySegment<byte> v   => AppendRecord.Create(v, "test-raw-schema"),
            ReadOnlySequence<byte> v => AppendRecord.Create(v, "test-raw-schema"),
            _                      => throw new InvalidOperationException($"Unhandled payload shape '{payload.GetType().Name}'.")
        };

        record.Stream = "test-stream";

        // Act
        record.EnsureValid();

        // Assert
        await Assert.That(record.DataFormat).IsEqualTo(SchemaDataFormat.Bytes);
        await Assert.That(record.SchemaName).IsEqualTo("test-raw-schema");
        await Assert.That(record.Value!.IsBytes()).IsTrue();
    }

    [Test]
    public void create_accepts_an_empty_raw_payload() {
        var record = AppendRecord.Create(ReadOnlyMemory<byte>.Empty, "test-tombstone-schema", r => r.Stream = "test-stream");

        record.EnsureValid();
    }

    [Test]
    public void ensure_valid_passes_when_null_value_has_bytes_format() {
        var record = new AppendRecord {
            Value      = null!,
            DataFormat = SchemaDataFormat.Bytes,
            SchemaName = "test-tombstone-schema",
            Stream     = "test-stream"
        };

        record.EnsureValid();
    }

    [Test]
    public async ValueTask ensure_valid_throws_when_null_value_has_a_non_bytes_format() {
        var record = new AppendRecord {
            Value      = null!,
            DataFormat = SchemaDataFormat.Json,
            Stream     = "test-stream"
        };

        var ex = Assert.Throws<ValidationException>(record.EnsureValid);

        await Assert.That(ex.Errors).Count().IsEqualTo(1);
        await Assert.That(ex.Errors[0].FieldName).IsEqualTo(nameof(AppendRecord.Value));
    }

    [Test]
    public async ValueTask ensure_valid_throws_when_a_non_bytes_format_carries_a_schema_name() {
        var record = new AppendRecord {
            Value      = "test-payload",
            DataFormat = SchemaDataFormat.Json,
            SchemaName = "test-schema",
            Stream     = "test-stream"
        };

        var ex = Assert.Throws<ValidationException>(record.EnsureValid);

        await Assert.That(ex.Errors).Count().IsEqualTo(1);
        await Assert.That(ex.Errors[0].FieldName).IsEqualTo(nameof(AppendRecord.SchemaName));
    }

    [Test]
    public async ValueTask ensure_valid_throws_when_bytes_format_has_a_whitespace_schema_name() {
        var record = new AppendRecord {
            Value      = new byte[] { 1, 2, 3 },
            DataFormat = SchemaDataFormat.Bytes,
            SchemaName = "   ",
            Stream     = "test-stream"
        };

        var ex = Assert.Throws<ValidationException>(record.EnsureValid);

        await Assert.That(ex.Errors[0].FieldName).IsEqualTo(nameof(AppendRecord.SchemaName));
    }

    [Test]
    public async ValueTask ensure_valid_collects_all_problems_at_once() {
        var record = new AppendRecord {
            RecordId   = RecordId.None,
            Value      = null!,
            DataFormat = SchemaDataFormat.Unspecified,
            Stream     = StreamName.Unset
        };

        var ex = Assert.Throws<ValidationException>(record.EnsureValid);

        // An unstated format is not a problem, so only the id and the value are collected.
        await Assert.That(ex.Errors).Count().IsEqualTo(2);
        await Assert.That(ex.Errors).Contains(p => p.FieldName == nameof(AppendRecord.RecordId));
        await Assert.That(ex.Errors).Contains(p => p.FieldName == nameof(AppendRecord.Value));
    }
}
