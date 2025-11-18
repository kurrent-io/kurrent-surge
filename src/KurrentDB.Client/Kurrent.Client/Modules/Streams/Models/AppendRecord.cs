namespace Kurrent.Client.Streams;

#pragma warning disable CS8604
[PublicAPI]
public record AppendRecord {
    public static readonly AppendRecord None = new() {
        RecordId   = Guid.Empty,
        Value      = null!,
        DataFormat = SchemaDataFormat.Unspecified,
        Metadata   = null!
    };

    public static AppendRecordBuilder New => new();

    /// <summary>
    /// The assigned record id.
    /// </summary>
    public Guid RecordId { get; internal init; } = Guid.NewGuid();

    /// <summary>
    /// The message payload.
    /// </summary>
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
    /// Creates a new <see cref="AppendRecord"/> instance with the specified value and data format.
    /// </summary>
    /// <param name="value">
    /// The content value to include in the message. This cannot be null and must align with the specified
    /// <paramref name="dataFormat"/> (e.g., when passing byte arrays, the format must be <see cref="SchemaDataFormat.Bytes"/>).
    /// </param>
    /// <param name="dataFormat">
    /// The format of the data represented by <paramref name="value"/>. Defaults to <see cref="SchemaDataFormat.Json"/>.
    /// Cannot be <see cref="SchemaDataFormat.Unspecified"/>.
    /// </param>
    public static AppendRecord Create(object value, SchemaDataFormat dataFormat = SchemaDataFormat.Json) =>
        New.WithValue(value).WithDataFormat(dataFormat).Build();
}
