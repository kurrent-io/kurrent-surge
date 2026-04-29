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

public sealed class InvalidStreamNameException(string reason, Exception? innerException = null)
    : KurrentException(ErrorCode.FromType<InvalidStreamNameException>(), reason, innerException)
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
