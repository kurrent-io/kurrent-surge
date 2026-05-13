using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents information about a stream in KurrentDB.
/// </summary>
[PublicAPI]
public record StreamInfo {
    public static readonly StreamInfo None = new();

    /// <summary>
    /// The name of the stream.
    /// </summary>
    public StreamName Stream { get; init; } = StreamName.Unset;

    /// <summary>
    /// The state of the stream, indicating whether it is active, deleted, tombstoned, or missing.
    /// </summary>
    public StreamState State { get; init; } = StreamState.Missing;

    /// <summary>
    /// The revision of the metadata stream, used for optimistic concurrency when updating configuration or metadata.
    /// </summary>
    public Revision InfoRevision { get; init; } = Revision.Unset;

    #region ->> Configuration <<-

    /// <summary>
    /// The retention configuration for the stream.
    /// </summary>
    public StreamRetention Retention { get; init; } = new();

    /// <summary>
    /// The access control list for the stream.
    /// </summary>
    public StreamAcl Acl { get; init; } = new();

    /// <summary>
    /// User-provided custom metadata associated with the stream.
    /// </summary>
    public Metadata Metadata { get; init; } = new();

    #endregion

    #region ->> First record <<-

    /// <summary>
    /// The revision of the first record in the stream.
    /// </summary>
    public Revision FirstStreamRevision { get; init; } = Revision.Unset;

    /// <summary>
    /// The log position of the first record in the stream.
    /// </summary>
    public LogPosition FirstStreamPosition { get; init; } = LogPosition.Unset;

    /// <summary>
    /// The timestamp when the first record was appended to the stream.
    /// </summary>
    public DateTime FirstStreamAppendedAt { get; init; } = DateTime.MinValue;

    #endregion

    #region ->> Last record <<-

    /// <summary>
    /// The revision of the last record in the stream.
    /// </summary>
    public Revision LastStreamRevision { get; init; } = Revision.Unset;

    /// <summary>
    /// The log position of the last record in the stream.
    /// </summary>
    public LogPosition LastStreamPosition { get; init; } = LogPosition.Unset;

    /// <summary>
    /// The timestamp when the last record was appended to the stream.
    /// </summary>
    public DateTime LastStreamAppendedAt { get; init; } = DateTime.MinValue;

    #endregion
}

/// <summary>
/// Represents the state of a stream in KurrentDB.
/// </summary>
/// <remarks>
/// These values also match the stream state codes defined in the KurrentDB protocol.
/// </remarks>
public enum StreamState {
    Active     = -4,
    Deleted    = -5,
    Tombstoned = -6,
    Missing    = -1
}

// public readonly record struct RecordState(RecordStateKind Kind, StreamName Stream) {                                                                                            
//     public static RecordState Deleted(StreamName stream) => new(RecordStateKind.Deleted, stream);                                                                                    
//     public static RecordState Tombstoned(StreamName stream) => new(RecordStateKind.Tombstoned, stream);                                                                                    
//     public static RecordState NotFound => new(RecordStateKind.NotFound, StreamName.Unset);                                                                           
// }                                                                                                      
//                                                                                                          
// public enum RecordStateKind {                                                                          
//     Deleted    = -5,                                                                                   
//     Tombstoned = -6,                                                                                   
//     NotFound   = -1                                                                                    
// }  

public abstract record RecordState(StreamName Stream) {                                                
    public sealed record Deleted(StreamName Stream) : RecordState(Stream);                             
    public sealed record Tombstoned(StreamName Stream) : RecordState(Stream);                          
    public sealed record NotFound() : RecordState(StreamName.Unset);                                   
}   

/// <summary>
/// Represents the retention configuration for a stream.
/// </summary>
[PublicAPI]
public record StreamRetention {
    public static readonly StreamRetention None = new();

    /// <summary>
    /// The optional maximum age of events allowed in the stream.
    /// </summary>
    public TimeSpan? MaxAge { get; init; }

    /// <summary>
    /// The optional maximum number of events allowed in the stream.
    /// </summary>
    public int? MaxCount { get; init; }

    public Revision TruncateBefore { get; init; } = Revision.Unset;

    public bool HasMaxAge => MaxAge.HasValue && MaxAge > TimeSpan.Zero;

    public bool HasMaxCount => MaxCount is >= 0;


    public static implicit operator StreamRetentionOptions(StreamRetention retention) =>
        new() { MaxAge = retention.MaxAge, MaxCount = retention.MaxCount };
}

/// <summary>
/// Represents the retention configuration for a stream.
/// </summary>
[PublicAPI]
public class StreamRetentionOptions {
    /// <summary>
    /// The optional maximum age of events allowed in the stream.
    /// </summary>
    public TimeSpan? MaxAge { get; set; }

    /// <summary>
    /// The optional maximum number of events allowed in the stream.
    /// </summary>
    public int? MaxCount { get; set; }

    /// <summary>
    /// Indicates whether the stream has a maximum age configured for its retention policy.
    /// </summary>
    public bool HasMaxAge => MaxAge.HasValue && MaxAge > TimeSpan.Zero;

    /// <summary>
    /// Indicates whether a maximum count restriction is defined for the stream retention.
    /// Returns <c>true</c> if <see cref="MaxCount"/> is greater than zero; otherwise, <c>false</c>.
    /// </summary>
    public bool HasMaxCount => MaxCount is >= 0;

    public static implicit operator StreamRetention(StreamRetentionOptions options) =>
        new() {
            MaxAge   = options.MaxAge,
            MaxCount = options.MaxCount
        };
}

/// <summary>
/// Represents an access control list for a stream
/// </summary>
[PublicAPI]
[DebuggerDisplay("{DebuggerDisplayValue,nq}")]
public record StreamAcl {
    /// <summary>
    /// Roles and users permitted to read the stream
    /// </summary>
    public string[] ReadRoles { get; init; } = [];

    /// <summary>
    /// Roles and users permitted to write to the stream
    /// </summary>
    public string[] WriteRoles { get; init; } = [];

    /// <summary>
    /// Roles and users permitted to delete the stream
    /// </summary>
    public string[] DeleteRoles { get; init; } = [];

    /// <summary>
    /// Roles and users permitted to read stream metadata
    /// </summary>
    public string[] MetaReadRoles { get; init; } = [];

    /// <summary>
    /// Roles and users permitted to write stream metadata
    /// </summary>
    public string[] MetaWriteRoles { get; init; } = [];

    public virtual bool Equals(StreamAcl? other) {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return ReadRoles.SequenceEqual(other.ReadRoles)
            && WriteRoles.SequenceEqual(other.WriteRoles)
            && DeleteRoles.SequenceEqual(other.DeleteRoles)
            && MetaReadRoles.SequenceEqual(other.MetaReadRoles)
            && MetaWriteRoles.SequenceEqual(other.MetaWriteRoles);
    }

    public override int GetHashCode() =>
        HashCode.Combine(ReadRoles, WriteRoles, DeleteRoles, MetaReadRoles, MetaWriteRoles);

    string DebuggerDisplayValue =>
        $"""
        Read:       [{string.Join(",", ReadRoles)}]
        Write:      [{string.Join(",", WriteRoles)}]
        Delete:     [{string.Join(",", DeleteRoles)}]
        MetaRead:   [{string.Join(",", MetaReadRoles)}]
        MetaWrite:  [{string.Join(",", MetaWriteRoles)}]
        """;
}

#region ->> Mappings  <<-

/// <summary>
/// Represents metadata associated with a stream in KurrentDB.
/// </summary>
[JsonConverter(typeof(StreamMetadataJsonConverter))]
record StreamMetadata {
    /// <summary>
    /// The optional maximum age of events allowed in the stream.
    /// </summary>
    public TimeSpan? MaxAge { get; init; }

    /// <summary>
    /// The optional <see cref="Revision"/> from which previous events can be scavenged.
    /// This is used to implement soft-deletion of streams.
    /// </summary>
    public Revision? TruncateBefore { get; init; }

    /// <summary>
    /// The optional amount of time for which the stream head is cacheable.
    /// </summary>
    public TimeSpan? CacheControl { get; init; }

    /// <summary>
    /// The optional maximum number of events allowed in the stream.
    /// </summary>
    public int? MaxCount { get; init; }

    /// <summary>
    /// The optional <see cref="JsonDocument"/> of user-provided metadata.
    /// </summary>
    public JsonDocument? CustomMetadata { get; init; }

    /// <summary>
    /// The optional <see cref="StreamAcl"/> for the stream.
    /// </summary>
    public StreamAcl? Acl { get; init; }

    public bool HasMaxAge         => MaxAge.HasValue && MaxAge > TimeSpan.Zero;
    public bool HasTruncateBefore => TruncateBefore is { };
    public bool HasCacheControl   => CacheControl.HasValue && CacheControl.Value > TimeSpan.Zero;
    public bool HasMaxCount       => MaxCount is > 0;
    public bool HasCustomMetadata => CustomMetadata is { } && CustomMetadata.RootElement.ValueKind != JsonValueKind.Undefined;
    public bool HasAcl            => Acl is { };
}

class StreamMetadataJsonConverter : JsonConverter<StreamMetadata> {
    const string MaxAgeKey         = "$maxAge";
    const string MaxCountKey       = "$maxCount";
    const string TruncateBeforeKey = "$tb";
    const string CacheControlKey   = "$cacheControl";
    const string AclKey            = "$acl";

    public static readonly StreamMetadataJsonConverter Instance = new();

    public override StreamMetadata Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        int?       maxCount       = null;
        TimeSpan?  maxAge         = null;
        TimeSpan?  cacheControl   = null;
        Revision?  truncateBefore = null;
        StreamAcl? acl            = null;

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new InvalidOperationException($"Expected start of object token, but got {reader.TokenType}.");

        writer.WriteStartObject();

        while (reader.Read()) {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) throw new InvalidOperationException();

            switch (reader.GetString()) {
                case MaxCountKey:
                    if (!reader.Read()) throw new InvalidOperationException();
                    maxCount = reader.GetInt32();
                    break;

                case MaxAgeKey:
                    if (!reader.Read()) throw new InvalidOperationException();
                    var int64 = reader.GetInt64();
                    maxAge = TimeSpan.FromSeconds(int64);
                    break;

                case CacheControlKey:
                    if (!reader.Read()) throw new InvalidOperationException();
                    cacheControl = TimeSpan.FromSeconds(reader.GetInt64());
                    break;

                case TruncateBeforeKey:
                    if (!reader.Read()) throw new InvalidOperationException();
                    var value = reader.GetInt64();
                    truncateBefore = value == long.MaxValue
                        ? Revision.Max
                        : Revision.From(value);

                    break;

                case AclKey:
                    if (!reader.Read()) throw new InvalidOperationException();
                    acl = StreamAclJsonConverter.Instance.Read(ref reader, typeof(StreamAcl), options);
                    break;

                default:
                    writer.WritePropertyName(reader.GetString()!);
                    reader.Read();
                    switch (reader.TokenType) {
                        case JsonTokenType.Comment:
                            writer.WriteCommentValue(reader.GetComment());
                            break;

                        case JsonTokenType.String:
                            writer.WriteStringValue(reader.GetString());
                            break;

                        case JsonTokenType.Number:
                            writer.WriteNumberValue(reader.GetDouble());
                            break;

                        case JsonTokenType.True:
                        case JsonTokenType.False:
                            writer.WriteBooleanValue(reader.GetBoolean());
                            break;

                        case JsonTokenType.Null:
                            reader.Read();
                            writer.WriteNullValue();
                            break;

                        default:
                            throw new JsonException(
                                $"Unexpected token type {reader.TokenType} for property '{reader.GetString()}' in stream metadata.");
                    }

                    break;
            }
        }

        writer.WriteEndObject();
        writer.Flush();

        stream.Position = 0;

        return new() {
            MaxCount       = maxCount,
            MaxAge         = maxAge,
            TruncateBefore = truncateBefore,
            CacheControl   = cacheControl,
            Acl            = acl,
            CustomMetadata = JsonDocument.Parse(stream)
        };
    }

    public override void Write(Utf8JsonWriter writer, StreamMetadata value, JsonSerializerOptions options) {
        writer.WriteStartObject();

        if (value.MaxCount is { } maxCount)
            writer.WriteNumber(MaxCountKey, maxCount);

        if (value.MaxAge is { } maxAge)
            writer.WriteNumber(MaxAgeKey, (long)maxAge.TotalSeconds);

        if (value.TruncateBefore is { } truncateBefore)
            writer.WriteNumber(TruncateBeforeKey, truncateBefore);

        if (value.CacheControl is { } cacheControl)
            writer.WriteNumber(CacheControlKey, (long)cacheControl.TotalSeconds);

        if (value.Acl is { } acl) {
            writer.WritePropertyName(AclKey);
            StreamAclJsonConverter.Instance.Write(writer, acl, options);
        }

        if (value.CustomMetadata is { } customMetadata)
            foreach (var property in customMetadata.RootElement.EnumerateObject())
                property.WriteTo(writer);

        writer.WriteEndObject();
    }
}

class StreamAclJsonConverter : JsonConverter<StreamAcl> {
    const string AclRead      = "$r";
    const string AclWrite     = "$w";
    const string AclDelete    = "$d";
    const string AclMetaRead  = "$mr";
    const string AclMetaWrite = "$mw";

    public static readonly StreamAclJsonConverter Instance = new();

    public override StreamAcl Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        string[]? read      = null;
        string[]? write     = null;
        string[]? delete    = null;
        string[]? metaRead  = null;
        string[]? metaWrite = null;

        if (reader.TokenType != JsonTokenType.StartObject) throw new InvalidOperationException();

        while (reader.Read()) {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new InvalidOperationException();

            switch (reader.GetString()) {
                case AclRead:
                    read = ReadRoles(ref reader);
                    break;

                case AclWrite:
                    write = ReadRoles(ref reader);
                    break;

                case AclDelete:
                    delete = ReadRoles(ref reader);
                    break;

                case AclMetaRead:
                    metaRead = ReadRoles(ref reader);
                    break;

                case AclMetaWrite:
                    metaWrite = ReadRoles(ref reader);
                    break;
            }
        }

        return new() {
            ReadRoles      = read ?? [],
            WriteRoles     = write ?? [],
            DeleteRoles    = delete ?? [],
            MetaReadRoles  = metaRead ?? [],
            MetaWriteRoles = metaWrite ?? []
        };
    }

    static string[]? ReadRoles(ref Utf8JsonReader reader) {
        if (!reader.Read())
            throw new InvalidOperationException();

        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
            return [reader.GetString()!];

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new InvalidOperationException();

        var roles = new List<string>();

        while (reader.Read()) {
            if (reader.TokenType == JsonTokenType.EndArray)
                return roles.Count == 0 ? [] : roles.ToArray();

            if (reader.TokenType != JsonTokenType.String)
                throw new InvalidOperationException();

            roles.Add(reader.GetString()!);
        }

        return roles.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, StreamAcl value, JsonSerializerOptions options) {
        writer.WriteStartObject();

        WriteRoles(writer, AclRead, value.ReadRoles);
        WriteRoles(writer, AclWrite, value.WriteRoles);
        WriteRoles(writer, AclDelete, value.DeleteRoles);
        WriteRoles(writer, AclMetaRead, value.MetaReadRoles);
        WriteRoles(writer, AclMetaWrite, value.MetaWriteRoles);

        writer.WriteEndObject();
    }

    static void WriteRoles(Utf8JsonWriter writer, string name, string[]? roles) {
        if (roles is null)
            return;

        writer.WritePropertyName(name);
        writer.WriteStartArray();

        foreach (var role in roles)
            writer.WriteStringValue(role);

        writer.WriteEndArray();
    }
}

#endregion
