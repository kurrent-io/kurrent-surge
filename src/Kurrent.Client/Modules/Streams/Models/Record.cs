using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kurrent.Client.Registry;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Streams.Checkpoints;

namespace Kurrent.Client.Streams;

[PublicAPI]
[DebuggerDisplay("{ToDebugString()}")]
public record Record {
	public static readonly Record None = new();

    public Record(IRecordDecoder? decoder = null) =>
        Decoder = decoder ?? NoOpRecordDecoder.Instance;

    IRecordDecoder Decoder { get; }
    
    /// <summary>
    /// Indicates whether the current <see cref="Record"/> instance is unset.
    /// </summary>
    public bool IsUnset => this == None;
    
	/// <summary>
    /// The unique identifier of the record.
    /// </summary>
	public Guid Id { get; init; } = Guid.Empty;

    /// <summary>
    /// The precise position of the record in the global transaction log and optionaly within its stream.
    /// </summary>
    public RecordPosition Position { get; init; } = RecordPosition.Unset;
    
	/// <summary>
	/// Represents the index link associated with the record.
	/// </summary>
	public Link Link { get; init; } = Link.Unset;

	/// <summary>
	/// When the record was created in the database.
	/// </summary>
	public DateTime Timestamp { get; init; }

	/// <summary>
	/// The metadata associated with the record, represented as a collection of key-value pairs.
	/// </summary>
	public Metadata Metadata { get; init; } = new();

    /// <summary>
    /// The sequential order of the record in the context of a read operation.
    /// </summary>
    /// <remarks>
    /// The sequence number is on every record returned from a read operation,
    /// providing a way to track the order of records as they are processed in particular scenarios,
    /// such as subscriptions or projections.
    /// </remarks>
    public SequenceId Sequence { get; init; } = SequenceId.Unset;

    /// <summary>
    /// Binary data representation of the record's value.
    /// Used to store the raw serialized content of the record.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; init; } = ReadOnlyMemory<byte>.Empty;

    /// <summary>
    /// The schema information associated with the record.
    /// </summary>
    public RecordSchemaInfo Schema { get; init; } = RecordSchemaInfo.None;

    /// <summary>
    /// Indicates whether the record's data has been redacted.
    /// </summary>
    public bool IsRedacted { get; init; }

	/// <summary>
	/// The deserialized message.
	/// </summary>
    [MaybeNull]
	public object Value { get; internal set; }

    /// <summary>
    /// The type of the deserialized message.
    /// </summary>
    public Type ValueType { get; internal set; } = Type.MissingType;

    /// <summary>
    /// Indicates whether the current <see cref="Record"/> instance has been successfully decoded.
    /// </summary>
    public bool IsDecoded => Value is { } && ValueType != Type.MissingType;
    
    /// <summary>
    /// Represents the stream associated with the record.
    /// </summary>
    public StreamName Stream => Position.Stream;

    /// <summary>
    /// Represents the revision of a stream at a specific point in time.
    /// </summary>
    public Revision Revision => Position.Revision;
    
    /// <summary>
    /// Represents the classification or grouping of an item.
    /// </summary>
    public string Category => Stream.Category;
    
    /// <summary>
    /// Lazily-serialized JSON representation of <see cref="Value"/> for filter expression evaluation.
    /// Returns <c>null</c> when the record is not decoded or the schema format is not JSON.
    /// </summary>
    internal JsonNode? DecodedValueAsJson => IsDecoded && Schema.SchemaFormat == SchemaDataFormat.Json
        ? field ??= JsonSerializer.SerializeToNode(Value)
        : null;
    
	/// <summary>
	/// Attempts to decode the record's data asynchronously.
	/// </summary>
	public ValueTask<bool> TryDecode(CancellationToken cancellationToken = default) =>
        Decoder.Decode(this, cancellationToken);
    
    /// <summary>
    /// Returns the decoded value cast to <typeparamref name="T"/> with zero overhead for reference types.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T As<T>() {
        var msg = Value!;
        return typeof(T).IsValueType ? (T)msg : Unsafe.As<object, T>(ref msg);
    }
    
    public override string ToString() => $"{Schema.SchemaName} {Revision}@{Stream} - {Position}";

	string ToDebugString() =>
		$"Record: Id={Id}, Position={Position}, Stream={Stream}, Revision={Revision}, " +
		$"Value={Value ?? "Not Decoded"}, ValueType={ValueType}";

}

public readonly record struct Link() {
    public static readonly Link Unset = new();

    public bool IsUnset => this == Unset;

    public SchemaName  Name     { get; init; } = SchemaName.Unset;
    public LogPosition Position { get; init; } = LogPosition.Unset;
    public Revision    Revision { get; init; } = Revision.Unset;
    public StreamName  Stream   { get; init; } = StreamName.Unset;
}

public interface IRecordDecoder {
	ValueTask<bool> Decode(Record record, CancellationToken ct);
}

sealed class RecordDecoder(ISchemaSerializerProvider serializerProvider, SchemaRegistryPolicy? registryPolicy = null) : IRecordDecoder {
    static readonly SchemaName LinkSchemaName = "$>";

    SchemaRegistryPolicy RegistryPolicy { get; } = registryPolicy ?? SchemaRegistryPolicy.NoRequirements;

    public async ValueTask<bool> Decode(Record record, CancellationToken ct) {
        if (record.IsDecoded)
            return true;

        if (record.Schema.SchemaName == LinkSchemaName) {
            record.Value = Encoding.UTF8.GetString(record.Data.Span);
            return true;
        }

        // we do not decode bytes - I wonder if we even need to have a bytes serializer at all
        // currently Deserialize would work and return the bytes as is, but that seems pointless.
        // because we do not want to copy data to value if its bytes.
        // full circle.
        if (record.Schema.SchemaFormat == SchemaDataFormat.Bytes)
            return false;

        var context = new SchemaSerializationContext {
            Stream               = record.Stream,
            RecordSchema         = record.Schema,
            SchemaRegistryPolicy = RegistryPolicy,
            CancellationToken    = ct
        };

        // the deserializer should not even return null.
        // either throws or returns a value or returns a result <T, Error>
        // why the fook would we allow nulls here ffs?!
        record.Value = await serializerProvider
            .GetSerializer(record.Schema.SchemaFormat)
            .Deserialize(record.Data, context)
            .ConfigureAwait(false)!;

        if (record.Value is not null)
            record.ValueType = record.Value.GetType();

        return true;
    }
}

sealed class NoOpRecordDecoder : IRecordDecoder {
    public static readonly IRecordDecoder Instance = new NoOpRecordDecoder();

    public ValueTask<bool> Decode(Record record, CancellationToken ct) => ValueTask.FromResult(false);
}
