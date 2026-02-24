using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Kurrent.Client.Registry;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Streams.Checkpoints;

namespace Kurrent.Client.Streams;

// public interface IRecord {
//     /// <summary>
//     /// The unique identifier of the record.
//     /// </summary>
//     Guid Id { get; init; }
//
//     /// <summary>
//     /// The precise position of the record in the global transaction log and optionaly within its stream.
//     /// </summary>
//     RecordPosition Position { get; init; }
//
//     /// <summary>
//     /// When the record was created in the database.
//     /// </summary>
//     DateTime Timestamp { get; init; }
//
//     /// <summary>
//     /// The metadata associated with the record, represented as a collection of key-value pairs.
//     /// </summary>
//     Metadata Metadata { get; init; }
//
//     /// <summary>
//     /// The sequential order of the record in the context of a read operation.
//     /// </summary>
//     /// <remarks>
//     /// The sequence number is on every record returned from a read operation,
//     /// providing a way to track the order of records as they are processed in particular scenarios,
//     /// such as subscriptions or projections.
//     /// </remarks>
//     SequenceId Sequence { get; init; }
//
//     /// <summary>
//     /// Binary data representation of the record's value.
//     /// Used to store the raw serialized content of the record.
//     /// </summary>
//     ReadOnlyMemory<byte> Data { get; init; }
//
//     /// <summary>
//     /// The schema information associated with the record.
//     /// </summary>
//     RecordSchemaInfo Schema { get; init; }
//
//     /// <summary>
//     /// Indicates whether the record's data has been redacted.
//     /// </summary>
//     bool IsRedacted { get; init; }
//
//     /// <summary>
//     /// The revision of the index associated with the record.
//     /// </summary>
//     Revision IndexRevision { get; init; }
//
//     /// <summary>
//     /// Represents the index link associated with the record.
//     /// </summary>
//     Link Link { get; init; }
//
//     /// <summary>
//     /// Represents the stream associated with the record.
//     /// </summary>
//     StreamName Stream => Position.Stream;
//
//     /// <summary>
//     /// Represents the revision of a stream at a specific point in time.
//     /// </summary>
//     Revision Revision => Position.Revision;
// }
//
// public record EncodedRecord : IRecord {
// 	public static readonly EncodedRecord None = new();
//
//     public EncodedRecord(IRecordDecoder? decoder = null) =>
//         Decoder = decoder ?? NoOpRecordDecoder.Instance;
//
//     IRecordDecoder Decoder { get; }
//
// 	/// <summary>
//     /// The unique identifier of the record.
//     /// </summary>
// 	public Guid Id { get; init; } = Guid.Empty;
//
//     /// <summary>
//     /// The precise position of the record in the global transaction log and optionaly within its stream.
//     /// </summary>
//     public RecordPosition Position { get; init; } = RecordPosition.Unset;
//
// 	/// <summary>
// 	/// The revision of the index associated with the record.
// 	/// </summary>
// 	public Revision IndexRevision { get; init; } = Revision.Unset;
//
// 	/// <summary>
// 	/// Represents the index link associated with the record.
// 	/// </summary>
// 	public Link Link { get; init; } = Link.None;
//
// 	/// <summary>
// 	/// When the record was created in the database.
// 	/// </summary>
// 	public DateTime Timestamp { get; init; }
//
// 	/// <summary>
// 	/// The metadata associated with the record, represented as a collection of key-value pairs.
// 	/// </summary>
// 	public Metadata Metadata { get; init; } = new();
//
//     /// <summary>
//     /// Binary data representation of the record's value.
//     /// Used to store the raw serialized content of the record.
//     /// </summary>
//     public ReadOnlyMemory<byte> Data { get; init; } = ReadOnlyMemory<byte>.Empty;
//
//     /// <summary>
//     /// The schema information associated with the record.
//     /// </summary>
//     public RecordSchemaInfo Schema { get; init; } = RecordSchemaInfo.None;
//
//     /// <summary>
//     /// The sequential order of the record in the context of a read operation.
//     /// </summary>
//     /// <remarks>
//     /// The sequence number is on every record returned from a read operation,
//     /// providing a way to track the order of records as they are processed in particular scenarios,
//     /// such as subscriptions or projections.
//     /// </remarks>
//     public SequenceId Sequence { get; init; } = SequenceId.None;
//
//     /// <summary>
//     /// Indicates whether the record's data has been redacted.
//     /// </summary>
//     public bool IsRedacted { get; init; }
//
//
//
//     // this whole logic is fucked.
//     // we do not want to copy data to value if it is bytes
//     // what we could do is to use a `field` to store the value if it exists and then
//     // Value would return the Data if its null.
//     //
//     // but then again, that could be confusing as hell.
//     // unless we use a new property to indicate that it does not have a value.
//
//     // /// <summary>
//     // /// Indicates whether the current <see cref="Record"/> instance has been successfully decoded.
//     // /// </summary>
//     // public bool IsDecoded => !Data.IsEmpty
//     //                       && Value is not null
//     //                       && ValueType != typeof(byte[])
//     //                       && ValueType != typeof(ReadOnlyMemory<byte>)
//     //                       && ValueType != typeof(Memory<byte>);
//
//     // // this makes more sense if we dont want to consider bytes as decoded
//     // public bool IsDecoded => !Data.IsEmpty
//     //                       && Schema.SchemaFormat != SchemaDataFormat.Bytes
//     //                       && ValueType != SystemTypes.MissingType;
//
//
// 	/// <summary>
// 	/// Attempts to decode the record's data asynchronously.
// 	/// </summary>
// 	public ValueTask<bool> TryDecode(CancellationToken cancellationToken = default) =>
//         Decoder.Decode(this, cancellationToken);
//
//     public override string ToString() => $"{Schema.SchemaName} {Revision}@{Stream} - {Position}";
// }
//
// [PublicAPI]
// public record DecodedRecord {
// 	public static readonly DecodedRecord None = new();
//
// 	/// <summary>
//     /// The unique identifier of the record.
//     /// </summary>
// 	public Guid Id { get; init; } = Guid.Empty;
//
//     /// <summary>
//     /// The precise position of the record in the global transaction log and optionaly within its stream.
//     /// </summary>
//     public RecordPosition Position { get; init; } = RecordPosition.Unset;
//
// 	/// <summary>
// 	/// When the record was created in the database.
// 	/// </summary>
// 	public DateTime Timestamp { get; init; }
//
// 	/// <summary>
// 	/// The metadata associated with the record, represented as a collection of key-value pairs.
// 	/// </summary>
// 	public Metadata Metadata { get; init; } = new();
//
//     /// <summary>
//     /// The sequential order of the record in the context of a read operation.
//     /// </summary>
//     /// <remarks>
//     /// The sequence number is on every record returned from a read operation,
//     /// providing a way to track the order of records as they are processed in particular scenarios,
//     /// such as subscriptions or projections.
//     /// </remarks>
//     public SequenceId Sequence { get; init; } = SequenceId.None;
//
//     /// <summary>
//     /// Binary data representation of the record's value.
//     /// Used to store the raw serialized content of the record.
//     /// </summary>
//     public ReadOnlyMemory<byte> Data { get; init; } = ReadOnlyMemory<byte>.Empty;
//
//     /// <summary>
//     /// The schema information associated with the record.
//     /// </summary>
//     public RecordSchemaInfo Schema { get; init; } = RecordSchemaInfo.None;
//
//     /// <summary>
//     /// Indicates whether the record's data has been redacted.
//     /// </summary>
//     public bool IsRedacted { get; init; }
//
// 	/// <summary>
// 	/// The deserialized message.
// 	/// </summary>
// 	public object Value { get; init; } = null!;
//
//     /// <summary>
//     /// The type of the deserialized message.
//     /// </summary>
//     public Type ValueType { get; init; } = SystemTypes.MissingType;
//
//     /// <summary>
//     /// The revision of the index associated with the record.
//     /// </summary>
//     public Revision IndexRevision { get; init; } = Revision.Unset;
//
//     /// <summary>
//     /// Represents the index link associated with the record.
//     /// </summary>
//     public Link Link { get; init; } = Link.None;
//
//     /// <summary>
//     /// Represents the stream associated with the record.
//     /// </summary>
//     public StreamName Stream => Position.Stream;
//
//     /// <summary>
//     /// Represents the revision of a stream at a specific point in time.
//     /// </summary>
//     public Revision Revision => Position.Revision;
//
//
//
//     // this whole logic is fucked.
//     // we do not want to copy data to value if it is bytes
//     // what we could do is to use a `field` to store the value if it exists and then
//     // Value would return the Data if its null.
//     //
//     // but then again, that could be confusing as hell.
//     // unless we use a new property to indicate that it does not have a value.
//
//     // /// <summary>
//     // /// Indicates whether the current <see cref="Record"/> instance has been successfully decoded.
//     // /// </summary>
//     // public bool IsDecoded => !Data.IsEmpty
//     //                       && Value is not null
//     //                       && ValueType != typeof(byte[])
//     //                       && ValueType != typeof(ReadOnlyMemory<byte>)
//     //                       && ValueType != typeof(Memory<byte>);
//
//     // // this makes more sense if we dont want to consider bytes as decoded
//     // public bool IsDecoded => !Data.IsEmpty
//     //                       && Schema.SchemaFormat != SchemaDataFormat.Bytes
//     //                       && ValueType != SystemTypes.MissingType;
//
//
// 	/// <summary>
// 	/// Attempts to decode the record's data asynchronously.
// 	/// </summary>
// 	public ValueTask<bool> TryDecode(CancellationToken cancellationToken = default) =>
//         Decoder.Decode(this, cancellationToken);
//
//     public override string ToString() => $"{Schema.SchemaName} {Revision}@{Stream} - {Position}";
//
// 	string ToDebugString() =>
// 		$"Record: Id={Id}, Position={Position}, Stream={Stream}, Revision={Revision}, " +
// 		$"Value={Value ?? "Not Decoded"}, ValueType={ValueType}";
// }

[PublicAPI]
[DebuggerDisplay("{ToDebugString()}")]
public record Record {
	public static readonly Record None = new();

    public Record(IRecordDecoder? decoder = null) =>
        Decoder = decoder ?? NoOpRecordDecoder.Instance;

    IRecordDecoder Decoder { get; }

	/// <summary>
    /// The unique identifier of the record.
    /// </summary>
	public Guid Id { get; init; } = Guid.Empty;

    /// <summary>
    /// The precise position of the record in the global transaction log and optionaly within its stream.
    /// </summary>
    public RecordPosition Position { get; init; } = RecordPosition.Unset;

	/// <summary>
	/// The revision of the index associated with the record.
	/// </summary>
	public Revision IndexRevision { get; init; } = Revision.Unset;

	/// <summary>
	/// Represents the index link associated with the record.
	/// </summary>
	public Link Link { get; init; } = Link.None;

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
    public SequenceId Sequence { get; init; } = SequenceId.None;

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
    public Type ValueType { get; internal set; } = SystemTypes.MissingType;

    /// <summary>
    /// Indicates whether the current <see cref="Record"/> instance has been successfully decoded.
    /// </summary>
    public bool IsDecoded => Value is not null;

    /// <summary>
    /// Indicates whether the current <see cref="Record"/> instance is unset.
    /// </summary>
    public bool IsUnset => this == None;


    /// <summary>
    /// Represents the stream associated with the record.
    /// </summary>
    public StreamName Stream => Position.Stream;

    /// <summary>
    /// Represents the revision of a stream at a specific point in time.
    /// </summary>
    public Revision Revision => Position.Revision;



    // this whole logic is fucked.
    // we do not want to copy data to value if it is bytes
    // what we could do is to use a `field` to store the value if it exists and then
    // Value would return the Data if its null.
    //
    // but then again, that could be confusing as hell.
    // unless we use a new property to indicate that it does not have a value.

    // /// <summary>
    // /// Indicates whether the current <see cref="Record"/> instance has been successfully decoded.
    // /// </summary>
    // public bool IsDecoded => !Data.IsEmpty
    //                       && Value is not null
    //                       && ValueType != typeof(byte[])
    //                       && ValueType != typeof(ReadOnlyMemory<byte>)
    //                       && ValueType != typeof(Memory<byte>);

    // // this makes more sense if we dont want to consider bytes as decoded
    // public bool IsDecoded => !Data.IsEmpty
    //                       && Schema.SchemaFormat != SchemaDataFormat.Bytes
    //                       && ValueType != SystemTypes.MissingType;


	/// <summary>
	/// Attempts to decode the record's data asynchronously.
	/// </summary>
	public ValueTask<bool> TryDecode(CancellationToken cancellationToken = default) =>
        Decoder.Decode(this, cancellationToken);

    public override string ToString() => $"{Schema.SchemaName} {Revision}@{Stream} - {Position}";

	string ToDebugString() =>
		$"Record: Id={Id}, Position={Position}, Stream={Stream}, Revision={Revision}, " +
		$"Value={Value ?? "Not Decoded"}, ValueType={ValueType}";
}

public readonly record struct Link() {
	public static readonly Link None = new();

	public SchemaName     Name     { get; init; } = SchemaName.None;
	public LogPosition    Position { get; init; } = LogPosition.Unset;
	public Revision Revision { get; init; } = Revision.Unset;
	public StreamName     Stream   { get; init; } = StreamName.None;
}

public interface IRecordDecoder {
	ValueTask<bool> Decode(Record record, CancellationToken ct);

    // ValueTask<DecodedRecord> DecodeOrDefault(EncodedRecord record, CancellationToken ct);
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

    // public ValueTask<DecodedRecord> DecodeOrDefault(EncodedRecord record, CancellationToken ct) {
    //     if (record.Schema.SchemaName == LinkSchemaName) {
    //         return new DecodedRecord {
    //             Id         = record.Id,
    //             Position   = record.Position,
    //             Timestamp  = record.Timestamp,
    //             Metadata   = record.Metadata,
    //             Sequence   = record.Sequence,
    //             Data       = record.Data,
    //             Schema     = record.Schema,
    //             IsRedacted = record.IsRedacted,
    //             Value      = Encoding.UTF8.GetString(record.Data.Span),
    //             ValueType  = typeof(string)
    //         };
    //     }
    //
    //     // we do not decode bytes - I wonder if we even need to have a bytes serializer at all
    //     // currently Deserialize would work and return the bytes as is, but that seems pointless.
    //     // because we do not want to copy data to value if its bytes.
    //     // full circle.
    //     if (record.Schema.SchemaFormat == SchemaDataFormat.Bytes)
    //         return false;
    //
    //     var context = new SchemaSerializationContext {
    //         Stream               = record.Stream,
    //         RecordSchema         = record.Schema,
    //         SchemaRegistryPolicy = RegistryPolicy,
    //         CancellationToken    = ct
    //     };
    //
    //     // the deserializer should not even return null.
    //     // either throws or returns a value or returns a result <T, Error>
    //     // why the fook would we allow nulls here ffs?!
    //     record.Value = await serializerProvider
    //         .GetSerializer(record.Schema.SchemaFormat)
    //         .Deserialize(record.Data, context)
    //         .ConfigureAwait(false)!;
    //
    //     if (record.Value is not null)
    //         record.ValueType = record.Value.GetType();
    //
    //     return true;
    // }
}


// sealed class RecordDecoder(ISchemaSerializerProvider serializerProvider, SchemaRegistryPolicy? registryPolicy = null) : IRecordDecoder {
// 	static readonly SchemaName LinkSchemaName = "$>";
//
// 	SchemaRegistryPolicy RegistryPolicy { get; } = registryPolicy ?? SchemaRegistryPolicy.NoRequirements;
//
// 	public async ValueTask<bool> Decode(Record record, CancellationToken ct) {
// 		if (record.IsDecoded)
// 			return true;
//
// 		if (record.Schema.SchemaName == LinkSchemaName) {
// 			record.Value = Encoding.UTF8.GetString(record.Data.Span);
// 			return true;
// 		}
//
//         // we do not decode bytes - I wonder if we even need to have a bytes serializer at all
//         // currently Deserialize would work and return the bytes as is, but that seems pointless.
//         // because we do not want to copy data to value if its bytes.
//         // full circle.
//         if (record.Schema.SchemaFormat == SchemaDataFormat.Bytes)
//             return false;
//
//         var context = new SchemaSerializationContext {
// 			Stream               = record.Stream,
//             RecordSchema         = record.Schema,
// 			SchemaRegistryPolicy = RegistryPolicy,
// 			CancellationToken    = ct
// 		};
//
//         // the deserializer should not even return null.
//         // either throws or returns a value or returns a result <T, Error>
//         // why the fook would we allow nulls here ffs?!
// 		record.Value = await serializerProvider
// 			.GetSerializer(record.Schema.SchemaFormat)
// 			.Deserialize(record.Data, context)
// 			.ConfigureAwait(false)!;
//
//         if (record.Value is not null)
//             record.ValueType = record.Value.GetType();
//
//         return true;
// 	}
// }

sealed class NoOpRecordDecoder : IRecordDecoder {
    public static readonly IRecordDecoder Instance = new NoOpRecordDecoder();

    public ValueTask<bool> Decode(Record record, CancellationToken ct) => ValueTask.FromResult(false);

    // public ValueTask<DecodedRecord> DecodeOrDefault(EncodedRecord record, CancellationToken ct) => throw new NotImplementedException();
}
