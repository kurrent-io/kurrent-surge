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
        Metadata   = new(),
        Stream     = StreamName.Unset
    };

    public static AppendRecordBuilder New => new();

    public static AppendRecordBuilder ToStream(StreamName stream) =>
        new AppendRecordBuilder().ForStream(stream);

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
    public StreamName Stream { get; internal init; } = StreamName.Unset;

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; internal init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Validates the record-level invariants of this <see cref="AppendRecord"/> instance and returns a <see cref="ValidationResult"/>
    /// indicating whether the instance is valid or contains validation problems.
    /// </summary>
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

        return problems.Count > 0
            ? ValidationError.Create(ErrorCode.FromType<AppendRecord>(), [.. problems])
            : Results.Success;
    }

    /// <summary>
    /// Validates this <see cref="AppendRecord"/> instance and throws a <see cref="ValidationException"/> if any validation problems are found.
    /// </summary>
    public AppendRecord EnsureValid() {
        Validate().Unwrap();
        return this;
    }
}
