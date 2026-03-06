using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents the name of a stream in KurrentDB.
/// </summary>
[PublicAPI]
public readonly record struct StreamName {
    /// <summary>
    /// Represents an uninitialized or empty stream name.
    /// </summary>
    public static readonly StreamName None = new("", StreamCategory.None, Partition.Any);

    /// <summary>
    /// A stream containing all events in the KurrentDB transaction file.
    /// </summary>
    public static readonly StreamName All = new(AllStream, StreamCategory.None, Partition.Any);

    /// <summary>
    /// The prefix character for system streams.
    /// </summary>
    public static readonly char SystemStreamPrefix = '$';

    /// <summary>
    /// The separator character for stream partitions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stream partitions are denoted by appending a '!' followed by the partition number.
    /// For example, "orders!1" represents partition 1 of the "orders" stream.
    /// </para>
    /// <para>
    /// The '!' character was chosen because it is:
    /// <list type="bullet">
    /// <item><description>URL safe (RFC 3986 sub-delimiter)</description></item>
    /// <item><description>Regex safe (no special meaning)</description></item>
    /// <item><description>URN safe (allowed in namespace-specific string)</description></item>
    /// <item><description>Easy to type</description></item>
    /// <item><description>Unlikely to appear naturally in stream names</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public static readonly char PartitionSeparator = '!';

    /// <summary>
    /// The default separator character for stream categories.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the default separator used by the KurrentDB $by_category projection.
    /// For configurable category parsing, use <see cref="CategoryParseOptions"/>.
    /// </para>
    /// </remarks>
    public static readonly char CategorySeparator = CategoryParseOptions.Default.Separator;

    /// <summary>
    /// A stream containing all events in the KurrentDB transaction file.
    /// </summary>
    public const string AllStream = "$all";

    /// <summary>
    /// A stream containing links pointing to each stream in the KurrentDB.
    /// </summary>
    public const string StreamsStream = "$streams";

    /// <summary>
    /// A stream containing system settings.
    /// </summary>
    public const string SettingsStream = "$settings";

    /// <summary>
    /// A stream containing statistics.
    /// </summary>
    public const string StatsStreamPrefix = "$stats";

    /// <summary>
    /// Metastreams are special streams that store metadata about the main stream.
    /// </summary>
    public const string MetastreamPrefix = "$$";

    /// <summary>
    /// Prefix for custom index streams.
    /// </summary>
    public const string IndexStreamsPrefix = "$idx";

    StreamName(string value, StreamCategory category, Partition partition, string partitionKeyValue = "") {
        Value          = value;
        StreamCategory = category;
        Partition      = partition;
    }

    /// <summary>
    /// The underlying string value of the stream.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Indicates whether the stream name is unset (i.e., equals <see cref="None"/>).
    /// </summary>
    public bool IsUnset => this == None;

    /// <summary>
    /// The category of the stream if applicable.
    /// </summary>
    public StreamCategory StreamCategory { get; }

    /// <summary>
    /// The stream partition if applicable.
    /// </summary>
    public Partition Partition { get; }

    /// <summary>
    /// The metastream associated with this stream.
    /// </summary>
    /// <remarks>
    /// Metastreams are special streams that store metadata about the main stream. They are typically prefixed with "$$".
    ///<para/>
    /// If this stream is in fact a metastream, it returns itself.
    /// </remarks>
    public StreamName Metastream =>
        IsMetastream ? this : new StreamName(MetastreamPrefix + Value, StreamCategory, Partition);

    public bool IsMetastream => Value.StartsWith(MetastreamPrefix);

    public bool IsSystemStream => Value.StartsWith(SystemStreamPrefix);

    public bool IsIndex => Value.StartsWith(IndexStreamsPrefix);

    public bool IsAllStream => Value == AllStream;

    public bool IsStreamsStream => Value == StreamsStream;

    public bool IsSettingsStream => Value == SettingsStream;

    public bool IsStatsStream => Value.StartsWith(StatsStreamPrefix);

    public override string ToString() => Value;

    /// <summary>
    /// Creates a <see cref="StreamName"/> from a string value.
    /// </summary>
    /// <param name="value">The stream name string to parse.</param>
    /// <param name="categoryOptions">
    /// Options for category extraction. If null, <see cref="CategoryParseOptions.Default"/> is used.
    /// </param>
    /// <returns>A <see cref="StreamName"/> with extracted category and partition information.</returns>
    /// <exception cref="InvalidStreamNameException">
    /// Thrown when the stream name is null/whitespace, contains multiple partition separators,
    /// has an invalid partition number, or is empty before the partition separator.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Parsing rules:
    /// <list type="bullet">
    /// <item><description>The partition is always after the '!' character (if present)</description></item>
    /// <item><description>The category is extracted based on <paramref name="categoryOptions"/> (separator and position)</description></item>
    /// <item><description>The stream name can contain any characters except multiple '!' separators</description></item>
    /// <item><description>Both partition and category are optional</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Examples with default options (separator: '-', position: First):
    /// <code>
    /// "orders-123!1"       => (Value: "orders-123!1", Category: "orders", Partition: 1)
    /// "orders/portugal!1"  => (Value: "orders/portugal!1", Category: "orders/portugal", Partition: 1)
    /// "orders!1"           => (Value: "orders!1", Category: "orders", Partition: 1)
    /// "orders-123"         => (Value: "orders-123", Category: "orders", Partition: Any)
    /// "orders"             => (Value: "orders", Category: "orders", Partition: Any)
    /// "users-GUID!4"       => (Value: "users-GUID!4", Category: "users", Partition: 4)
    /// </code>
    /// </para>
    /// <para>
    /// Examples with position: Last:
    /// <code>
    /// "shopping-cart-1"    => (Value: "shopping-cart-1", Category: "shopping-cart", Partition: Any)
    /// "user-profile-v2!3"  => (Value: "user-profile-v2!3", Category: "user-profile", Partition: 3)
    /// </code>
    /// </para>
    /// </remarks>
    public static StreamName From(string? value, CategoryParseOptions? categoryOptions = null) {
        if (string.IsNullOrWhiteSpace(value))
            InvalidStreamNameException.Throw("Stream name cannot be null or whitespace.");

        var span           = value.AsSpan();
        var partitionIndex = span.LastIndexOf(PartitionSeparator);

        if (partitionIndex >= 0 && span.IndexOf(PartitionSeparator) != partitionIndex)
            InvalidStreamNameException.Throw(
                $"Stream name contains multiple partition separators ('!'). " +
                $"Partition can only be specified once at the end (e.g., 'orders!1'): '{value}'.");

        Partition          partition;
        ReadOnlySpan<char> streamSpan;

        if (partitionIndex >= 0) {
            var partitionSpan = span[(partitionIndex + 1)..];

            if (!int.TryParse(partitionSpan, out var partitionNumber))
                InvalidStreamNameException.Throw(
                    $"Partition must be a valid number. " +
                    $"Use the '!' separator followed by a numeric partition value (e.g., 'orders!1'): '{value}'.");

            partition  = Partition.From(partitionNumber);
            streamSpan = span[..partitionIndex];

            if (streamSpan.IsEmpty)
                InvalidStreamNameException.Throw($"Stream name cannot be empty before the partition separator ('!'): '{value}'.");
        }
        else {
            partition  = Partition.Any;
            streamSpan = span;
        }

        // Extract category using configurable options
        var category = StreamCategory.Parse(streamSpan, categoryOptions);

        return new(value, category, partition);
    }

    /// <summary>
    /// Implicitly converts a string to a <see cref="StreamName"/>.
    /// </summary>
    public static implicit operator StreamName(string stream) => From(stream);

    /// <summary>
    /// Implicitly converts a <see cref="StreamName"/> to its string representation.
    /// </summary>
    public static implicit operator string(StreamName stream) => stream.ToString();

    /// <summary>
    /// Implicitly converts a <see cref="StreamName"/> to its <see cref="StreamCategory"/>.
    /// </summary>
    public static implicit operator StreamCategory(StreamName stream) => stream.StreamCategory;

    /// <summary>
    /// Implicitly converts a <see cref="StreamName"/> to its <see cref="Partition"/>.
    /// </summary>
    public static implicit operator Partition(StreamName stream) => stream.Partition;
}

public sealed class InvalidStreamNameException(string message, Exception? innerException = null)
    : KurrentException(ErrorCode.FromType<InvalidStreamNameException>(), message, innerException), IThrowableException<InvalidStreamNameException> {
    public static InvalidStreamNameException Create(string message, Exception? innerException) => new(message, innerException);
}

public interface IThrowableException<out TSelf> where TSelf : Exception, IThrowableException<TSelf> {
    static abstract TSelf Create(string message, Exception? innerException = null);
}

public static class ThrowableExceptionExtensions {
    extension<T>(IThrowableException<T>) where T : Exception, IThrowableException<T> {
        [DoesNotReturn, StackTraceHidden]
        public static void Throw(string message, Exception? innerException = null) =>
            throw T.Create(message, innerException);
    }
}
