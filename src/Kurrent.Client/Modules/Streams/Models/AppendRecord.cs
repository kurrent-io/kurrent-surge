using System.Diagnostics.CodeAnalysis;
using Kurrent.Validation;
using Resources;

namespace Kurrent.Client.Streams;

#pragma warning disable CS8604

[PublicAPI]
public record AppendRecord : SelfValidating {
    public static readonly AppendRecord None = new() {
        RecordId   = Guid.Empty,
        Value      = null!,
        DataFormat = SchemaDataFormat.Unspecified,
        Metadata   = new(),
        Stream     = StreamName.Unset
    };

    public static Builder New => new();

    /// <summary>
    /// The assigned record id.
    /// </summary>
    public Guid RecordId {
        get;
        init
        { field = value;
          ResetValidation(); }
    } = Guid.NewGuid();

    /// <summary>
    /// The message payload.
    /// </summary>
    [MaybeNull]
    public object Value {
        get;
        init
        { field = value;
          ResetValidation(); }
    } = null!;

    /// <summary>
    /// Specifies the format of the schema associated with the message.
    /// </summary>
    public SchemaDataFormat DataFormat {
        get;
        internal init
        { field = value;
          ResetValidation(); }
    } = SchemaDataFormat.Json;

    /// <summary>
    /// Optional metadata associated with the record.
    /// </summary>
    public Metadata Metadata { get; init; } = new();

    /// <summary>
    /// The stream to which this record will be appended.
    /// </summary>
    public StreamName Stream {
        get;
        init
        { field = value;
          ResetValidation(); }
    } = StreamName.Unset;

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public override ValidationResult RunValidation() {
        var problems = new List<ValidationProblem>();

        if (RecordId == Guid.Empty)
            problems.Add(ValidationProblem.CreateForField(nameof(RecordId), ErrorMessages.AppendRecordRecordIdRequired, RecordId));

        if (Value is null)
            problems.Add(ValidationProblem.CreateForField(nameof(Value), ErrorMessages.AppendRecordValueRequired));

        if (DataFormat == SchemaDataFormat.Unspecified)
            problems.Add(ValidationProblem.CreateForField(nameof(DataFormat), ErrorMessages.AppendRecordDataFormatRequired, DataFormat));

        if (Value is { } && Value.IsBytes() && DataFormat != SchemaDataFormat.Bytes)
            problems.Add(
                ValidationProblem.CreateForField(
                    nameof(DataFormat),
                    ErrorMessages.AppendRecordByteValueRequiresBytesFormat, DataFormat));

        return problems.Count > 0
            ? ValidationError.Create(ErrorCode.FromType<AppendRecord>(), [.. problems])
            : Results.Success;
    }

    public class Builder {
        AppendRecord _appendRecord = new();

        internal Builder FromRecord(AppendRecord record) {
            _appendRecord = record;
            return this;
        }

        public Builder ForStream(StreamName stream) {
            _appendRecord = _appendRecord with { Stream = stream };
            return this;
        }

        public Builder WithId(Guid recordId) {
            _appendRecord = _appendRecord with { RecordId = recordId };
            return this;
        }

        public Builder WithValue(object recordValue) {
            _appendRecord = _appendRecord with { Value = recordValue };
            return this;
        }

        public Builder WithDataFormat(SchemaDataFormat dataFormat) {
            _appendRecord = _appendRecord with { DataFormat = dataFormat };
            return this;
        }

        public Builder WithMetadata(Metadata metadata) {
            _appendRecord = _appendRecord with { Metadata = metadata };
            return this;
        }

        public Builder WithMetadata<T>(string key, T value) =>
            WithMetadata(_appendRecord.Metadata.With(key, value));

        public Builder WithTimestamp(DateTimeOffset timestamp) {
            _appendRecord = _appendRecord with { Timestamp = timestamp };
            return this;
        }

        public Builder WithTimestamp(long timestamp) =>
            WithTimestamp(DateTimeOffset.FromUnixTimeMilliseconds(timestamp));

        public AppendRecord Build() {
            _appendRecord.EnsureValid();

            return _appendRecord;
        }
    }
}