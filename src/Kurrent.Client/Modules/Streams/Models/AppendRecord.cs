using System.Diagnostics.CodeAnalysis;
using Kurrent.Schema;
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

    public static Builder New() => new();

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

    protected override ValidationResult RunValidation() {
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
            ? ValidationResult.Failure(ErrorCode.FromType<AppendRecord>(), [.. problems])
            : Results.Success;
    }

    public class Builder(AppendRecord record) : FluentBuilder<Builder, AppendRecord>(record) {
        public Builder() : this(new()) { }
        
        /// <inheritdoc />
        protected override AppendRecord OnBuild(AppendRecord record) {
            record.EnsureValid();
            return record;
        }

        public Builder Stream(StreamName stream) =>
            With(r => r with { Stream = stream });

        public Builder Id(Guid recordId) =>
            With(r => r with { RecordId = recordId });

        public Builder Value(object recordValue) =>
            With(r => r with { Value = recordValue });

        public Builder DataFormat(SchemaDataFormat dataFormat) =>
            With(r => r with { DataFormat = dataFormat });

        public Builder AddMetadata(Metadata metadata) =>
            With(r => r with { Metadata = metadata });

        public Builder AddMetadata<T>(string key, T value) =>
            With(r => r with { Metadata = r.Metadata.With(key, value) });

        public Builder Timestamp(DateTimeOffset timestamp) =>
            With(r => r with { Timestamp = timestamp });

        public Builder Timestamp(long timestamp) =>
            Timestamp(DateTimeOffset.FromUnixTimeMilliseconds(timestamp));
    }
}