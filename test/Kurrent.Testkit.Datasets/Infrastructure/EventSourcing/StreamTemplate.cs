using System.Runtime.CompilerServices;

namespace EventSourcing;

/// <summary>
/// A composite format template for constructing and categorizing stream identifiers.
/// The category is extracted from the first segment of the format string, split by the separator.
/// <example>
/// <code>
/// StreamTemplate schema = new("$registry/schemas/{0}");
/// schema.Category;              // "$registry"
/// schema.GetStream("user-v1");  // "$registry/schemas/user-v1"
///
/// StreamTemplate group = new("$registry/groups/{0}");
/// group.GetStream(someGuid);    // "$registry/groups/7a3f..."
/// </code>
/// </example>
/// </summary>
public record StreamTemplate {
    public StreamTemplate(string format, char categorySeparator = '/') {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentOutOfRangeException.ThrowIfEqual(categorySeparator, '\0');

        Format   = format;
        Category = Format.Split(categorySeparator, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    }

    public string Format { get; }

    public string Category { get; }

    public bool HasCategory => Category.Length > 0;

    public string GetStream(string id) =>
        FormattableStringFactory.Create(Format, id).ToString();

    public static implicit operator StreamTemplate(string format) =>
        new(format);

    public static implicit operator string(StreamTemplate template) =>
        template.Format;
}