using System.Diagnostics.CodeAnalysis;
using Kurrent.Surge.Schema;
using Kurrent.Validation;

namespace Kurrent.Surge.Client.Streams;

#pragma warning disable CS8604

[PublicAPI]
public record AppendRecord : SelfValidating {
    public static readonly AppendRecord None = new() {
        RecordId   = RecordId.None,
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
    public RecordId RecordId {
        get;
        set {
            field = value;
            ResetValidation();
        }
    } = RecordId.New();

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
    /// Optional schema name for the record. When set, it overrides the name resolved from the
    /// value's type through the message catalog. Raw payloads have no type mapping to resolve a
    /// name from, and the server rejects an empty one — they need this set explicitly.
    /// </summary>
    public string? SchemaName {
        get;
        set {
            field = value;
            ResetValidation();
        }
    }

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
        // Lazy list: the valid case is the hot path and must not allocate.
        List<ValidationProblem>? problems = null;

        if (RecordId == RecordId.None)
            (problems ??= []).Add(ValidationProblem.CreateForField(nameof(RecordId), "Record ID cannot be empty.", RecordId));

        if (Value is null)
            (problems ??= []).Add(ValidationProblem.CreateForField(nameof(Value), "Record value cannot be null."));

        if (DataFormat == SchemaDataFormat.Unspecified)
            (problems ??= []).Add(ValidationProblem.CreateForField(nameof(DataFormat), "Schema format cannot be Unspecified.", DataFormat));

        if (Value is { } && Value.IsBytes() && DataFormat != SchemaDataFormat.Bytes)
            (problems ??= []).Add(
                ValidationProblem.CreateForField(
                    nameof(DataFormat),
                    "Schema format must be 'Bytes' when the value is a byte array.", DataFormat));

        return problems is not null
            ? ValidationResult.Failure(ErrorCode.FromType<AppendRecord>(), [.. problems])
            : Results.Success;
    }
}
