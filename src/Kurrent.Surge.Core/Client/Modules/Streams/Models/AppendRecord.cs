using System.Diagnostics.CodeAnalysis;
using Kurrent.Schema;
using Kurrent.Validation;

namespace Kurrent.Client.Streams;

#pragma warning disable CS8604

[PublicAPI]
public record AppendRecord : SelfValidating {
    public static readonly AppendRecord None = new() {
        RecordId   = Guid.Empty,
        Value      = null!,
        DataFormat = SchemaDataFormat.Unspecified,
        Metadata   = new Metadata().Lock(),
        Stream     = StreamName.Unset
    };

    /// <summary>
    /// Creates a record carrying <paramref name="value"/> as its payload. Every other field takes its
    /// default; the target stream is supplied by the append operation when left unset.
    /// </summary>
    public static AppendRecord Create(object value) =>
        new() { Value = value };

    /// <summary>
    /// Creates a record carrying <paramref name="value"/> as its payload, then applies
    /// <paramref name="configure"/> to set the stream, data format, metadata, or any other field.
    /// </summary>
    public static AppendRecord Create(object value, Action<AppendRecord> configure) {
        var record = new AppendRecord { Value = value };
        configure(record);
        return record;
    }

    /// <summary>
    /// The assigned record id.
    /// </summary>
    public Guid RecordId {
        get;
        set {
            field = value;
            ResetValidation();
        }
    } = Guid.NewGuid();

    /// <summary>
    /// The message payload.
    /// </summary>
    [MaybeNull]
    public object Value {
        get;
        set {
            field = value;
            ResetValidation();
        }
    } = null!;

    /// <summary>
    /// Specifies the format of the schema associated with the message.
    /// </summary>
    public SchemaDataFormat DataFormat {
        get;
        set {
            field = value;
            ResetValidation();
        }
    } = SchemaDataFormat.Json;

    /// <summary>
    /// Optional metadata associated with the record.
    /// </summary>
    public Metadata Metadata { get; set; } = new();

    /// <summary>
    /// The stream to which this record will be appended.
    /// </summary>
    public StreamName Stream {
        get;
        set {
            field = value;
            ResetValidation();
        }
    } = StreamName.Unset;

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    protected override ValidationResult RunValidation() {
        var problems = new List<ValidationProblem>();

        if (RecordId == Guid.Empty)
            problems.Add(ValidationProblem.CreateForField(nameof(RecordId), "Record ID cannot be empty.", RecordId));

        if (Value is null)
            problems.Add(ValidationProblem.CreateForField(nameof(Value), "Record value cannot be null."));

        if (DataFormat == SchemaDataFormat.Unspecified)
            problems.Add(ValidationProblem.CreateForField(nameof(DataFormat), "Schema format cannot be Unspecified.", DataFormat));

        if (Value is { } && Value.IsBytes() && DataFormat != SchemaDataFormat.Bytes)
            problems.Add(
                ValidationProblem.CreateForField(
                    nameof(DataFormat),
                    "Schema format must be 'Bytes' when the value is a byte array.", DataFormat));

        return problems.Count > 0
            ? ValidationResult.Failure(ErrorCode.FromType<AppendRecord>(), [.. problems])
            : Results.Success;
    }
}
