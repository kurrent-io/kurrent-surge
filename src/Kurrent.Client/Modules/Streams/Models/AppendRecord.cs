using System.Diagnostics.CodeAnalysis;
using Kurrent.Validation;
using Resources;

namespace Kurrent.Client.Streams;

#pragma warning disable CS8604

[PublicAPI]
public record AppendRecord : ValidatableRecord {
    public static readonly AppendRecord None = new() {
        RecordId   = Guid.Empty,
        Value      = null!,
        DataFormat = SchemaDataFormat.Unspecified,
        Metadata   = new(),
        Stream     = StreamName.Unset
    };

    public static AppendRecordBuilder New => new();

    public static AppendRecordBuilder ToStream(StreamName stream) =>
        new AppendRecordBuilder().ForStream(stream);

    /// <summary>
    /// The assigned record id.
    /// </summary>
    public Guid RecordId {
        get;
        internal init {
            field = value;
            Invalidate();
        }
    } = Guid.NewGuid();

    /// <summary>
    /// The message payload.
    /// </summary>
    [MaybeNull]
    public object Value {
        get;
        internal init {
            field = value;
            Invalidate();
        }
    } = null!;

    /// <summary>
    /// Specifies the format of the schema associated with the message.
    /// </summary>
    public SchemaDataFormat DataFormat {
        get;
        internal init {
            field = value;
            Invalidate();
        }
    } = SchemaDataFormat.Json;

    /// <summary>
    /// Optional metadata associated with the record.
    /// </summary>
    public Metadata Metadata { get; internal init; } = new();

    /// <summary>
    /// The stream to which this record will be appended.
    /// </summary>
    public StreamName Stream {
        get;
        internal init {
            field = value;
            Invalidate();
        }
    } = StreamName.Unset;

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; internal init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Validates this <see cref="AppendRecord"/> instance and throws a <see cref="ValidationException"/> if any validation problems are found.
    /// Returns this instance for fluent chaining.
    /// </summary>
    public new AppendRecord EnsureValid() {
        GetValidationResult().Unwrap();
        return this;
    }

    protected override ValidationResult Validate() {
        var problems = new List<ValidationProblem>();

        if (RecordId == Guid.Empty)
            problems.Add(
                ValidationProblem.CreateForField(nameof(RecordId), ErrorMessages.AppendRecordRecordIdRequired, RecordId));

        if (Value is null)
            problems.Add(
                ValidationProblem.CreateForField(nameof(Value), ErrorMessages.AppendRecordValueRequired));

        if (DataFormat == SchemaDataFormat.Unspecified)
            problems.Add(
                ValidationProblem.CreateForField(nameof(DataFormat), ErrorMessages.AppendRecordDataFormatRequired, DataFormat));

        if (Value is not null && Value.IsBytes() && DataFormat != SchemaDataFormat.Bytes)
            problems.Add(
                ValidationProblem.CreateForField(nameof(DataFormat),
                    ErrorMessages.AppendRecordByteValueRequiresBytesFormat, DataFormat));

        return problems.Count > 0
            ? ValidationError.Create(ErrorCode.FromType<AppendRecord>(), [.. problems])
            : Results.Success;
    }
}

[PublicAPI]
public class AppendRecordBuilder {
    AppendRecord _appendRecord = new();

    internal AppendRecordBuilder FromRecord(AppendRecord record) {
        _appendRecord = record;
        return this;
    }

    public AppendRecordBuilder ForStream(StreamName stream) {
        _appendRecord = _appendRecord with { Stream = stream };
        return this;
    }

    public AppendRecordBuilder WithId(Guid recordId) {
        _appendRecord = _appendRecord with { RecordId = recordId };
        return this;
    }

    public AppendRecordBuilder WithValue(object recordValue) {
        _appendRecord = _appendRecord with { Value = recordValue };
        return this;
    }

    public AppendRecordBuilder WithDataFormat(SchemaDataFormat dataFormat) {
        _appendRecord = _appendRecord with { DataFormat = dataFormat };
        return this;
    }

    public AppendRecordBuilder WithMetadata(Metadata metadata) {
        _appendRecord = _appendRecord with {
            Metadata = metadata
        };

        return this;
    }

    public AppendRecordBuilder WithMetadata<T>(string key, T value) =>
        WithMetadata(_appendRecord.Metadata.With(key, value));

    public AppendRecordBuilder WithTimestamp(DateTimeOffset timestamp) {
        _appendRecord = _appendRecord with { Timestamp = timestamp };
        return this;
    }

    public AppendRecordBuilder WithTimestamp(long timestamp) =>
        WithTimestamp(DateTimeOffset.FromUnixTimeMilliseconds(timestamp));

    public AppendRecord Build() => _appendRecord.EnsureValid();
}

