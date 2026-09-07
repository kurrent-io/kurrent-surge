using Kurrent.Toolkit;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Validation;

namespace Kurrent.Surge.Client.Streams;

#pragma warning disable CS8604

[PublicAPI]
public record AppendRecord : SelfValidatingRecord {
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
    public static AppendRecord Create(object value, Func<AppendRecord, AppendRecord> configure) =>
        configure(new AppendRecord { Value = value });

    /// <summary>
    /// Creates a raw-payload record carrying <paramref name="value"/> under
    /// <paramref name="schemaName"/>. The format is <see cref="SchemaDataFormat.Bytes"/>, which
    /// bypasses catalog resolution, so the name is supplied here instead of resolved from a type.
    /// An empty payload is a valid raw record.
    /// </summary>
    public static AppendRecord Create(ReadOnlyMemory<byte> value, SchemaName schemaName) =>
        new() {
            Value      = value,
            DataFormat = SchemaDataFormat.Bytes,
            SchemaName = schemaName
        };

    /// <summary>
    /// Creates a raw-payload record carrying <paramref name="value"/> under
    /// <paramref name="schemaName"/>, then applies <paramref name="configure"/> to set the
    /// stream, metadata, or any other field.
    /// </summary>
    public static AppendRecord Create(ReadOnlyMemory<byte> value, SchemaName schemaName, Func<AppendRecord, AppendRecord> configure) =>
        configure(Create(value, schemaName));

    /// <summary>
    /// Creates a raw-payload record carrying <paramref name="value"/> under
    /// <paramref name="schemaName"/>. A multi-segment sequence is copied when the payload is
    /// framed; a single-segment one is not.
    /// </summary>
    public static AppendRecord Create(ReadOnlySequence<byte> value, SchemaName schemaName) =>
        new() {
            Value      = value,
            DataFormat = SchemaDataFormat.Bytes,
            SchemaName = schemaName
        };

    /// <summary>
    /// Creates a raw-payload record carrying <paramref name="value"/> under
    /// <paramref name="schemaName"/>, then applies <paramref name="configure"/> to set the
    /// stream, metadata, or any other field.
    /// </summary>
    public static AppendRecord Create(ReadOnlySequence<byte> value, SchemaName schemaName, Func<AppendRecord, AppendRecord> configure) =>
        configure(Create(value, schemaName));

    /// <summary>
    /// The assigned record id.
    /// </summary>
    public RecordId RecordId { get; init; } = RecordId.New();

    /// <summary>
    /// The message payload.
    /// </summary>
    [MaybeNull]
    public object Value { get; init; } = null!;

    /// <summary>
    /// The wire format for the payload. A stated format is used as given; left unstated, it is
    /// taken from the message type.
    /// </summary>
    public SchemaDataFormat DataFormat { get; init; } = SchemaDataFormat.Unspecified;

    /// <summary>
    /// The schema name for a raw payload. A <see cref="SchemaDataFormat.Bytes"/> record bypasses
    /// catalog resolution and has no type to resolve a name from, so it carries its name here.
    /// Every other format takes its name from the message catalog.
    /// </summary>
    internal string? SchemaName { get; init; }

    /// <summary>
    /// Optional metadata associated with the record.
    /// </summary>
    public Metadata Metadata { get; init; } = new();

    /// <summary>
    /// The stream to which this record will be appended.
    /// </summary>
    public StreamName Stream { get; init; } = StreamName.Unset;

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    protected override ValidationResult RunValidation() {
        // Lazy list: the valid case is the hot path and must not allocate.
        List<ValidationProblem>? problems = null;

        if (RecordId == RecordId.None)
            (problems ??= []).Add(ValidationProblem.CreateForField(nameof(RecordId), "Record ID cannot be empty.", RecordId));

        if (Value is null && DataFormat != SchemaDataFormat.Bytes)
            (problems ??= []).Add(
                ValidationProblem.CreateForField(
                    nameof(Value),
                    "Record value cannot be null unless the schema format is 'Bytes'."));

        // An unstated format is valid here: the write pipeline settles it from the message type.
        // A binary payload is the one case it cannot cover, and the next rule already rejects it.
        if (Value is { } && Value.IsBytes() && DataFormat != SchemaDataFormat.Bytes)
            (problems ??= []).Add(
                ValidationProblem.CreateForField(
                    nameof(DataFormat),
                    "Schema format must be 'Bytes' when the value is a byte array.", DataFormat));

        // Bytes bypasses catalog resolution, so nothing else can supply a name and the
        // server rejects an empty one.
        if (DataFormat == SchemaDataFormat.Bytes && string.IsNullOrWhiteSpace(SchemaName))
            (problems ??= []).Add(
                ValidationProblem.CreateForField(
                    nameof(SchemaName),
                    "Schema name is required when the schema format is 'Bytes'.", SchemaName));

        if (SchemaName is not null && DataFormat != SchemaDataFormat.Bytes)
            (problems ??= []).Add(
                ValidationProblem.CreateForField(
                    nameof(SchemaName),
                    "Schema name applies only to the 'Bytes' format; every other format takes its name from the message catalog.", SchemaName));

        return problems is not null
            ? ValidationResult.Failure(Errors.ErrorCode.FromType<AppendRecord>(), [.. problems])
            : new Success();
    }
}
