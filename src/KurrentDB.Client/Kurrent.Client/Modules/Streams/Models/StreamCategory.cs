namespace Kurrent.Client.Streams;

/// <summary>
/// Represents a stream category in KurrentDB.
/// </summary>
/// <remarks>
/// <para>
/// Categories are derived from stream names using a separator character.
/// For example, with separator '-' and position 'first', stream "order-123" has category "order".
/// </para>
/// <para>
/// The category extraction behavior can be configured to match your KurrentDB $by_category projection settings.
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct StreamCategory {
    /// <summary>
    /// Represents an empty or unset category.
    /// </summary>
    public static readonly StreamCategory None = new("");

    internal StreamCategory(string value) => Value = value;

    /// <summary>
    /// The category name value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Returns the category name as a string.
    /// </summary>
    public override string ToString() => Value;

    /// <summary>
    /// Parses a category from a stream name using the specified options.
    /// </summary>
    /// <param name="streamName">The stream name to extract the category from.</param>
    /// <param name="options">
    /// Options controlling how the category is extracted.
    /// If null, <see cref="CategoryParseOptions.Default"/> is used (separator: '-', position: First).
    /// </param>
    /// <returns>A <see cref="StreamCategory"/> extracted from the stream name.</returns>
    /// <exception cref="InvalidCategoryNameException">Thrown when the stream name is null or whitespace.</exception>
    /// <remarks>
    /// <para>
    /// Examples with default options (separator: '-', position: First):
    /// <code>
    /// "order-123"           → "order"
    /// "shopping-cart-1"     → "shopping"
    /// "orders"              → "orders"
    /// </code>
    /// </para>
    /// <para>
    /// Examples with position: Last:
    /// <code>
    /// "shopping-cart-1"     → "shopping-cart"
    /// "user-profile-v2"     → "user-profile"
    /// </code>
    /// </para>
    /// </remarks>
    public static StreamCategory Parse(string? streamName, CategoryParseOptions? options = null) {
        if (string.IsNullOrWhiteSpace(streamName))
            InvalidCategoryNameException.Throw("Stream name cannot be null or whitespace.");

        return Parse(streamName.AsSpan(), options);
    }

    /// <summary>
    /// Parses a category from a stream name span using the specified options.
    /// </summary>
    /// <param name="streamName">The stream name span to extract the category from.</param>
    /// <param name="options">
    /// Options controlling how the category is extracted.
    /// If null, <see cref="CategoryParseOptions.Default"/> is used (separator: '-', position: First).
    /// </param>
    /// <returns>A <see cref="StreamCategory"/> extracted from the stream name.</returns>
    /// <exception cref="InvalidCategoryNameException">Thrown when the stream name is empty.</exception>
    public static StreamCategory Parse(ReadOnlySpan<char> streamName, CategoryParseOptions? options = null) {
        if (streamName.IsEmpty)
            InvalidCategoryNameException.Throw("Stream name cannot be empty.");

        var opts = options ?? CategoryParseOptions.Default;

        var separatorIndex = opts.SeparatorPosition == CategorySeparatorPosition.First
            ? streamName.IndexOf(opts.Separator)
            : streamName.LastIndexOf(opts.Separator);

        // No separator found or separator at start - entire name is category
        return separatorIndex <= 0
            ? new(streamName.ToString())
            : new(streamName[..separatorIndex].ToString());
    }

    public static implicit operator StreamCategory(string _) => Parse(_);
    public static implicit operator string(StreamCategory _) => _.ToString();
}


/// <summary>
/// Determines which occurrence of the separator to use when extracting a category from a stream name.
/// </summary>
/// <remarks>
/// This matches the KurrentDB $by_category projection configuration.
/// </remarks>
[PublicAPI]
public enum CategorySeparatorPosition {
    /// <summary>
    /// Use the first occurrence of the separator.
    /// </summary>
    /// <remarks>
    /// <para>Example: "account-9E763770-0A8D-456D-AF23" → category "account"</para>
    /// </remarks>
    First,

    /// <summary>
    /// Use the last occurrence of the separator.
    /// </summary>
    /// <remarks>
    /// <para>Example: "shopping-cart-1" → category "shopping-cart"</para>
    /// </remarks>
    Last
}

/// <summary>
/// Options for parsing stream categories from stream names.
/// </summary>
/// <remarks>
/// <para>
/// These options match the KurrentDB $by_category projection configuration parameters.
/// </para>
/// <para>
/// The $by_category projection has two configuration parameters:
/// <list type="bullet">
/// <item><description><b>Position:</b> first or last — determines which occurrence of the separator to use</description></item>
/// <item><description><b>Separator:</b> any single character (default: -)</description></item>
/// </list>
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct CategoryParseOptions() {
    const char DefaultSeparator = '-';

    /// <summary>
    /// Default options: separator '-' with first position.
    /// </summary>
    public static readonly CategoryParseOptions Default = new();

    /// <summary>
    /// The separator character used to delimit the category from the rest of the stream name.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> <see cref="DefaultSeparator"/> ('-')</para>
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

/// <summary>
/// Exception thrown when a category name is invalid.
/// </summary>
public sealed class InvalidCategoryNameException(string message, Exception? innerException = null)
    : Exception(message, innerException), IThrowableException<InvalidCategoryNameException> {
    public static InvalidCategoryNameException Create(string message, Exception? innerException) => new(message, innerException);
}
