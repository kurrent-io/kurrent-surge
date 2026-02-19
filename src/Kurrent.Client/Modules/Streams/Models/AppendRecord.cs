using System.Diagnostics.CodeAnalysis;
using Kurrent.Validation;
using Resources;

namespace Kurrent.Client.Streams;

#pragma warning disable CS8604

[PublicAPI]
public record AppendRecord {
    public static readonly AppendRecord None = new() {
        RecordId   = Guid.Empty,
        Value      = null!,
        DataFormat = SchemaDataFormat.Unspecified,
        Metadata   = null!,
        Stream     = StreamName.None
    };

    public static AppendRecordBuilder New => new();

    /// <summary>
    /// The assigned record id.
    /// </summary>
    public Guid RecordId { get; internal init; } = Guid.NewGuid();

    /// <summary>
    /// The message payload.
    /// </summary>
    [MaybeNull]
    public object Value { get; internal init; } = null!;

    /// <summary>
    /// Specifies the format of the schema associated with the message.
    /// </summary>
    public SchemaDataFormat DataFormat { get; internal init; } = SchemaDataFormat.Json;

    /// <summary>
    /// Optional metadata associated with the record.
    /// </summary>
    public Metadata Metadata { get; internal init; } = new();

    /// <summary>
    /// The stream to which this record will be appended.
    /// </summary>
    public StreamName Stream { get; internal init; } = StreamName.None;

    public ValidationResult Validate() {
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

        if (Stream == StreamName.None)
            problems.Add(
                ValidationProblem.CreateForField(nameof(Stream), ErrorMessages.AppendRecordStreamRequired, Stream));

        return problems.Count > 0
            ? ValidationError.Create(ErrorCode.FromType<AppendRecord>(), [.. problems])
            : Results.Success;
    }

    public AppendRecord EnsureValid() {
        Validate().Unwrap();
        return this;
    }
}
