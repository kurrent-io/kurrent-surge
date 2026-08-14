using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Streams;

[PublicAPI]
[DebuggerDisplay("{ToDebugString()}")]
public record Record {
	public static readonly Record None = new();

    public Record(IRecordDecoder? decoder = null) =>
        Decoder = decoder ?? NullableRecordDecoder.Instance;

    IRecordDecoder Decoder { get; }
    
    /// <summary>
    /// Indicates whether the current <see cref="Record"/> instance is unset.
    /// </summary>
    public bool IsUnset => this == None;
    
	/// <summary>
    /// The unique identifier of the record.
    /// </summary>
	public RecordId Id { get; init; } = RecordId.None;

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
    /// Binary data representation of the record's value.
    /// Used to store the raw serialized content of the record.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; init; } = ReadOnlyMemory<byte>.Empty;

    /// <summary>
    /// The schema information associated with the record.
    /// </summary>
    public RecordSchemaInfo Schema { get; internal set; } = RecordSchemaInfo.None;

    /// <summary>
    /// Indicates whether this record is a link event whose resolved target was unavailable.
    /// </summary>
    public bool IsLink { get; init; }

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
    /// The name of the record's type. Returns the CLR type name when decoded,
    /// otherwise falls back to the wire-level schema name.
    /// </summary>
    public string TypeName => IsDecoded ? ValueType.Name : Schema.Schema.Value;

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
    /// Lazily-projected JSON representation of <see cref="Value"/> for filter expression evaluation.
    /// The decoder owns the projection — it holds the JSON serialization context, which keeps that
    /// context (and its transitive serialization dependencies) out of the contract layer. Cached per
    /// record. Returns <c>null</c> when the record is not decoded or its format is not JSON.
    /// </summary>
    internal JsonNode? DecodedValueAsJson => field ??= Decoder.ProjectToJson(this);
    
	/// <summary>
	/// Attempts to decode the record's data. The outcome reflects the record's state after
	/// the call: <see cref="DecodeOutcome.Decoded"/> means <see cref="Value"/> is available.
	/// Idempotent, and never throws for unknown schema names.
	/// </summary>
	public ValueTask<DecodeOutcome> TryDecode(CancellationToken cancellationToken = default) =>
        Decoder.TryDecode(this, cancellationToken);
    
    /// <summary>
    /// Returns the decoded value cast to <typeparamref name="T"/> with zero overhead for reference types.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T As<T>() {
        var msg = Value!;
        return typeof(T).IsValueType ? (T)msg : Unsafe.As<object, T>(ref msg);
    }
    
    public override string ToString() => $"{Schema.Schema} {Revision}@{Stream} - {Position}";

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
	ValueTask<DecodeOutcome> TryDecode(Record record, CancellationToken ct);

	/// <summary>
	/// Projects the decoded value to a <see cref="JsonNode"/> for filter expression evaluation, or
	/// <c>null</c> when the record is not decoded or its format is not JSON. Implementations own the
	/// JSON serialization context, keeping it out of the contract layer.
	/// </summary>
	JsonNode? ProjectToJson(Record record);
}

sealed class NullableRecordDecoder : IRecordDecoder {
    public static readonly IRecordDecoder Instance = new NullableRecordDecoder();

    public ValueTask<DecodeOutcome> TryDecode(Record record, CancellationToken ct) =>
        ValueTask.FromResult(DecodeOutcome.Unavailable);

    public JsonNode? ProjectToJson(Record record) => null;
}

/// <summary>
/// The outcome of a record decode attempt. Failures (deserialization errors, governance
/// violations) are exceptions — this enum covers only the non-exceptional outcomes, so
/// callers can distinguish "decoded" from "passed through raw" at the call site.
/// </summary>
public enum DecodeOutcome {
    /// <summary>The value was materialized (or the record was already decoded).</summary>
    Decoded,

    /// <summary>The payload is empty — there is nothing to decode.</summary>
    NoData,

    /// <summary>
    /// The wire schema name has no mapped type in the client's catalogs; the record stays
    /// raw. Map the type in a message catalog or handle the raw payload.
    /// </summary>
    UnknownType,

    /// <summary>
    /// Decoding is not applicable to this record: no decoder is attached (e.g.
    /// <c>SkipDecoding</c> reads, hand-built records, <see cref="Record.None"/>), or the
    /// payload is raw bytes that <see cref="Record.Data"/> already exposes (the
    /// <c>Bytes</c> format pass-through — there is nothing to materialize).
    /// </summary>
    Unavailable
}
