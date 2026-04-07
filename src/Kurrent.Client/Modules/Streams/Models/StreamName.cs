using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents the name of a stream in KurrentDB.
/// </summary>
[PublicAPI]
public readonly record struct StreamName {
    public static readonly StreamName Unset = new() {
        Value            = "",
        Category         = "",
        IsSystemStream   = false,
        IsMetadataStream = false,
        IsIndexStream    = false
    };

    internal static readonly StreamName Log = new() {
        Value            = "$all",
        Category         = "",
        IsSystemStream   = true,
        IsMetadataStream = false,
        IsIndexStream    = false
    };

    const char   SystemStreamPrefix         = '$';
    const string MetadataStreamPrefix       = "$$";
    const string SystemMetadataStreamPrefix = "$$$";
    const string IndexStreamPrefix          = "$idx";

    static readonly string[] SystemProjectedStreams = [
        "$ce",       // by category
        "$et",       // by event type
        "$bc",       // by correlation id
        "$category", // stream by category
        "$streams"   // all streams
    ];

    public string Value { get; private init; }

    /// <summary>
    /// The category of the stream if applicable.
    /// </summary>
    public string Category { get; private init; }

    /// <summary>
    /// The metastream associated with this stream.
    /// </summary>
    public StreamName Metastream =>
        IsMetadataStream
            ? this
            : new() {
                Value            = MetadataStreamPrefix + Value,
                Category         = "",
                IsSystemStream   = true,
                IsMetadataStream = true,
                IsIndexStream    = false
            };

    /// <summary>
    /// Indicates whether the stream is the metadata stream for a main stream.
    /// </summary>
    public bool IsMetadataStream { get; private init; }

    /// <summary>
    /// Indicates whether the stream is a system stream.
    /// </summary>
    public bool IsSystemStream { get; private init;}

    /// <summary>
    /// Indicates whether the stream is an index stream.
    /// </summary>
    public bool IsIndexStream { get; private init;}

    /// <summary>
    /// Indicates whether the value is uninitialized (i.e., equals <see cref="Unset"/>).
    /// </summary>
    public bool IsUnset => this == Unset;

    public override string ToString() => Value;

    public static StreamName From(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            InvalidStreamNameException.Throw("Stream name cannot be null or whitespace.");

        var isSystemStream    = value.StartsWith(SystemStreamPrefix);
        var isIndexStream     = value.StartsWith(IndexStreamPrefix);
        var isMetadataStream  = value.StartsWith(SystemMetadataStreamPrefix) || value.StartsWith(MetadataStreamPrefix);
        var isProjectedStream = SystemProjectedStreams.Contains(value, StringComparer.OrdinalIgnoreCase);

        var category = !isSystemStream && !isIndexStream && !isMetadataStream && !isProjectedStream
            ? ParseCategory(value) : "";

        return new() {
            Value            = value,
            Category         = category,
            IsSystemStream   = isSystemStream,
            IsIndexStream    = isIndexStream,
            IsMetadataStream = isMetadataStream
        };

        static string ParseCategory(ReadOnlySpan<char> streamName, char separator = '-', CategorySeparatorPosition separatorPosition = CategorySeparatorPosition.First) {
            var separatorIndex = separatorPosition == CategorySeparatorPosition.First
                ? streamName.IndexOf(separator)
                : streamName.LastIndexOf(separator);

            // No separator found or separator at start - entire name is category
            var category = separatorIndex <= 0
                ? streamName
                : streamName[..separatorIndex];

            return category.ToString();
        }
    }

    public static implicit operator StreamName(string stream) => From(stream);
    public static implicit operator string(StreamName stream) => stream.Value;
}

public sealed class InvalidStreamNameException(string message, Exception? innerException = null)
    : KurrentException(ErrorCode.FromType<InvalidStreamNameException>(), message, innerException)
    , IThrowableException<InvalidStreamNameException> {
    public static InvalidStreamNameException Create(string message, Exception? innerException) => new(message, innerException);
}

/// <summary>
/// Determines which occurrence of the separator to use when extracting a category from a stream name.
/// </summary>
/// <remarks>
/// This should match the <b>$by_category</b> projection configuration.
/// </remarks>
/// <stream>Accounts-UK-9E763770</stream>
/// <first>Accounts</first>
/// <last>Accounts-UK</last>
enum CategorySeparatorPosition {
    /// <summary>
    /// The first occurrence of the separator.
    /// </summary>
    /// <stream>Accounts-UK-9E763770</stream>
    /// <category>Accounts</category>
    First,

    /// <summary>
    /// The last occurrence of the separator.
    /// </summary>
    /// <stream>Accounts-UK-9E763770</stream>
    /// <category>Accounts-UK</category>
    Last
}

readonly record struct CategoryParseOptions() {
    const char DefaultSeparator = '-';

    public static readonly CategoryParseOptions Default = new();

    /// <summary>
    /// The separator character used to delimit the category from the rest of the stream name.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> '-'</para>
    /// <para>
    /// This should match your KurrentDB $by_category projection separator configuration.
    /// </para>
    /// </remarks>
    public char Separator { get; init; } = DefaultSeparator;

    /// <summary>
    /// Determines which occurrence of the separator to use when extracting the category.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> <see cref="CategorySeparatorPosition.First"/></para>
    /// <para>
    /// <list type="bullet">
    /// <item><description><b>First:</b> "account-9E763770-0A8D" → category "account"</description></item>
    /// <item><description><b>Last:</b> "shopping-cart-1" → category "shopping-cart"</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public CategorySeparatorPosition SeparatorPosition { get; init; } = CategorySeparatorPosition.First;
}


// public static class SystemStreams {
//     /// <summary>
//     /// A stream containing all records in the KurrentDB transaction file.
//     /// </summary>
//     public const string Log = "$all";
//
//     /// <summary>
//     /// A stream containing links pointing to each stream in the KurrentDB.
//     /// </summary>
//     public const string StreamsStream = "$streams";
//
//     /// <summary>
//     /// A stream containing system settings.
//     /// </summary>
//     public const string Settings = "$settings";
//
//     /// <summary>
//     /// All records per category, determined by the $by_category projection.
//     /// </summary>
//     public const string CategoriesStream = "$idx-ce";
//
//     /// <summary>
//     /// All records of a given type, determined by the $by_event_type projection.
//     /// </summary>
//     public const string RecordTypesStream = "$idx-et"; // should give alias to match record so "$idx-rs-{record-schema-name}"
//
//     public static class Prefixes {
//         /// <summary>
//         /// Any stream that is owned by the system.
//         /// </summary>
//         public static readonly char SystemStreamPrefix = '$';
//
//         /// <summary>
//         /// Streams containing statistics.
//         /// </summary>
//         public const string StatsStreamPrefix = "$stats";
//
//         /// <summary>
//         /// Streams that store metadata about a given stream.
//         /// </summary>
//         public const string MetastreamPrefix = "$$";
//
//         /// <summary>
//         /// Custom index streams.
//         /// </summary>
//         public const string IndexStreamsPrefix = "$idx";
//     }
// }









// using System.Diagnostics;
// using System.Diagnostics.CodeAnalysis;
// using Kurrent.Client.Exceptions;
//
// namespace Kurrent.Client.Streams;
//
// /// <summary>
// /// Represents the name of a stream in KurrentDB.
// /// </summary>
// [PublicAPI]
// public readonly record struct StreamName {
//     /// <summary>
//     /// Represents an uninitialized or empty stream name.
//     /// </summary>
//     public static readonly StreamName Unset = new("", StreamCategory.Unset, Partition.Any);
//
//     /// <summary>
//     /// A stream containing all events in the KurrentDB transaction file.
//     /// </summary>
//     public static readonly StreamName Log = new(AllStream, StreamCategory.Unset, Partition.Any);
//
//     /// <summary>
//     /// The prefix character for system streams.
//     /// </summary>
//     public static readonly char SystemStreamPrefix = '$';
//
//     /// <summary>
//     /// The separator character for stream partitions.
//     /// </summary>
//     /// <remarks>
//     /// <para>
//     /// Stream partitions are denoted by appending a '!' followed by the partition number.
//     /// For example, "orders!1" represents partition 1 of the "orders" stream.
//     /// </para>
//     /// <para>
//     /// The '!' character was chosen because it is:
//     /// <list type="bullet">
//     /// <item><description>URL safe (RFC 3986 sub-delimiter)</description></item>
//     /// <item><description>Regex safe (no special meaning)</description></item>
//     /// <item><description>URN safe (allowed in namespace-specific string)</description></item>
//     /// <item><description>Easy to type</description></item>
//     /// <item><description>Unlikely to appear naturally in stream names</description></item>
//     /// </list>
//     /// </para>
//     /// </remarks>
//     public static readonly char PartitionSeparator = '!';
//
//     /// <summary>
//     /// The default separator character for stream categories.
//     /// </summary>
//     /// <remarks>
//     /// <para>
//     /// This is the default separator used by the KurrentDB $by_category projection.
//     /// For configurable category parsing, use <see cref="CategoryParseOptions"/>.
//     /// </para>
//     /// </remarks>
//     public static readonly char CategorySeparator = CategoryParseOptions.Default.Separator;
//
//     /// <summary>
//     /// A stream containing all events in the KurrentDB transaction file.
//     /// </summary>
//     public const string AllStream = "$all";
//
//     /// <summary>
//     /// A stream containing links pointing to each stream in the KurrentDB.
//     /// </summary>
//     public const string StreamsStream = "$streams";
//
//     /// <summary>
//     /// A stream containing system settings.
//     /// </summary>
//     public const string SettingsStream = "$settings";
//
//     /// <summary>
//     /// A stream containing statistics.
//     /// </summary>
//     public const string StatsStreamPrefix = "$stats";
//
//     /// <summary>
//     /// Metastreams are special streams that store metadata about the main stream.
//     /// </summary>
//     public const string MetastreamPrefix = "$$";
//
//     /// <summary>
//     /// Prefix for custom index streams.
//     /// </summary>
//     public const string IndexStreamsPrefix = "$idx";
//
//     StreamName(string value, StreamCategory category, Partition partition, string partitionKeyValue = "") {
//         Value          = value;
//         StreamCategory = category;
//         Partition      = partition;
//     }
//
//     /// <summary>
//     /// The underlying string value of the stream.
//     /// </summary>
//     public string Value { get; }
//
//     /// <summary>
//     /// Indicates whether the stream name is unset (i.e., equals <see cref="None"/>).
//     /// </summary>
//     public bool IsUnset => this == Unset;
//
//     /// <summary>
//     /// The category of the stream if applicable.
//     /// </summary>
//     public StreamCategory StreamCategory { get; }
//
//     /// <summary>
//     /// The stream partition if applicable.
//     /// </summary>
//     public Partition Partition { get; }
//
//     /// <summary>
//     /// The metastream associated with this stream.
//     /// </summary>
//     /// <remarks>
//     /// Metastreams are special streams that store metadata about the main stream. They are typically prefixed with "$$".
//     ///<para/>
//     /// If this stream is in fact a metastream, it returns itself.
//     /// </remarks>
//     public StreamName Metastream =>
//         IsMetastream ? this : new StreamName(MetastreamPrefix + Value, StreamCategory, Partition);
//
//     public bool IsMetastream => Value.StartsWith(MetastreamPrefix);
//
//     public bool IsSystemStream => Value.StartsWith(SystemStreamPrefix);
//
//     public bool IsIndex => Value.StartsWith(IndexStreamsPrefix);
//
//     public bool IsAllStream => Value == AllStream;
//
//     public bool IsStreamsStream => Value == StreamsStream;
//
//     public bool IsSettingsStream => Value == SettingsStream;
//
//     public bool IsStatsStream => Value.StartsWith(StatsStreamPrefix);
//
//     public override string ToString() => Value;
//
//     /// <summary>
//     /// Creates a <see cref="StreamName"/> from a string value.
//     /// </summary>
//     /// <param name="value">The stream name string to parse.</param>
//     /// <param name="categoryOptions">
//     /// Options for category extraction. If null, <see cref="CategoryParseOptions.Default"/> is used.
//     /// </param>
//     /// <returns>A <see cref="StreamName"/> with extracted category and partition information.</returns>
//     /// <exception cref="InvalidStreamNameException">
//     /// Thrown when the stream name is null/whitespace, contains multiple partition separators,
//     /// has an invalid partition number, or is empty before the partition separator.
//     /// </exception>
//     /// <remarks>
//     /// <para>
//     /// Parsing rules:
//     /// <list type="bullet">
//     /// <item><description>The partition is always after the '!' character (if present)</description></item>
//     /// <item><description>The category is extracted based on <paramref name="categoryOptions"/> (separator and position)</description></item>
//     /// <item><description>The stream name can contain any characters except multiple '!' separators</description></item>
//     /// <item><description>Both partition and category are optional</description></item>
//     /// </list>
//     /// </para>
//     /// <para>
//     /// Examples with default options (separator: '-', position: First):
//     /// <code>
//     /// "orders-123!1"       => (Value: "orders-123!1", Category: "orders", Partition: 1)
//     /// "orders/portugal!1"  => (Value: "orders/portugal!1", Category: "orders/portugal", Partition: 1)
//     /// "orders!1"           => (Value: "orders!1", Category: "orders", Partition: 1)
//     /// "orders-123"         => (Value: "orders-123", Category: "orders", Partition: Any)
//     /// "orders"             => (Value: "orders", Category: "orders", Partition: Any)
//     /// "users-GUID!4"       => (Value: "users-GUID!4", Category: "users", Partition: 4)
//     /// </code>
//     /// </para>
//     /// <para>
//     /// Examples with position: Last:
//     /// <code>
//     /// "shopping-cart-1"    => (Value: "shopping-cart-1", Category: "shopping-cart", Partition: Any)
//     /// "user-profile-v2!3"  => (Value: "user-profile-v2!3", Category: "user-profile", Partition: 3)
//     /// </code>
//     /// </para>
//     /// </remarks>
//     public static StreamName From(string? value, CategoryParseOptions? categoryOptions = null) {
//         if (string.IsNullOrWhiteSpace(value))
//             InvalidStreamNameException.Throw("Stream name cannot be null or whitespace.");
//
//         var span           = value.AsSpan();
//         var partitionIndex = span.LastIndexOf(PartitionSeparator);
//
//         if (partitionIndex >= 0 && span.IndexOf(PartitionSeparator) != partitionIndex)
//             InvalidStreamNameException.Throw(
//                 $"Stream name contains multiple partition separators ('!'). " +
//                 $"Partition can only be specified once at the end (e.g., 'orders!1'): '{value}'.");
//
//         Partition          partition;
//         ReadOnlySpan<char> streamSpan;
//
//         if (partitionIndex >= 0) {
//             var partitionSpan = span[(partitionIndex + 1)..];
//
//             if (!int.TryParse(partitionSpan, out var partitionNumber))
//                 InvalidStreamNameException.Throw(
//                     $"Partition must be a valid number. " +
//                     $"Use the '!' separator followed by a numeric partition value (e.g., 'orders!1'): '{value}'.");
//
//             partition  = Partition.From(partitionNumber);
//             streamSpan = span[..partitionIndex];
//
//             if (streamSpan.IsEmpty)
//                 InvalidStreamNameException.Throw($"Stream name cannot be empty before the partition separator ('!'): '{value}'.");
//         }
//         else {
//             partition  = Partition.Any;
//             streamSpan = span;
//         }
//
//         // Extract category using configurable options
//         var category = StreamCategory.Parse(streamSpan, categoryOptions);
//
//         return new(value, category, partition);
//     }
//
//     /// <summary>
//     /// Implicitly converts a string to a <see cref="StreamName"/>.
//     /// </summary>
//     public static implicit operator StreamName(string stream) => From(stream);
//
//     /// <summary>
//     /// Implicitly converts a <see cref="StreamName"/> to its string representation.
//     /// </summary>
//     public static implicit operator string(StreamName stream) => stream.ToString();
//
//     /// <summary>
//     /// Implicitly converts a <see cref="StreamName"/> to its <see cref="StreamCategory"/>.
//     /// </summary>
//     public static implicit operator StreamCategory(StreamName stream) => stream.StreamCategory;
//
//     /// <summary>
//     /// Implicitly converts a <see cref="StreamName"/> to its <see cref="Partition"/>.
//     /// </summary>
//     public static implicit operator Partition(StreamName stream) => stream.Partition;
// }
//
// public sealed class InvalidStreamNameException(string message, Exception? innerException = null)
//     : KurrentException(ErrorCode.FromType<InvalidStreamNameException>(), message, innerException), IThrowableException<InvalidStreamNameException> {
//     public static InvalidStreamNameException Create(string message, Exception? innerException) => new(message, innerException);
// }
//
// public interface IThrowableException<out TSelf> where TSelf : Exception, IThrowableException<TSelf> {
//     static abstract TSelf Create(string message, Exception? innerException = null);
// }
//
// public static class ThrowableExceptionExtensions {
//     extension<T>(IThrowableException<T>) where T : Exception, IThrowableException<T> {
//         [DoesNotReturn, StackTraceHidden]
//         public static void Throw(string message, Exception? innerException = null) =>
//             throw T.Create(message, innerException);
//     }
// }
