using System.Runtime.CompilerServices;

namespace EventSourcing;

/// <summary>
/// The StreamTemplate class represents a format for constructing,
/// parsing, and managing stream identifiers.
/// This class provides functionality for handling stream templates
/// with optional category-based organization.
/// </summary>
public record StreamTemplate {
    public StreamTemplate(string format, char categorySeparator = '/') {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentOutOfRangeException.ThrowIfNegative(categorySeparator);

        Format   = format;
        Category = Format.Split(categorySeparator, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    }

    public string Format   { get; }
    public string Category { get; }

    public bool HasCategory => Category.Length > 0;

    public string GetStream(string id) =>
        FormattableStringFactory.Create(Format, id).ToString();

    public string GetStream(Guid id) => GetStream(id.ToString());

    public static implicit operator StreamTemplate(string format) =>
        new StreamTemplate(format);

    public static implicit operator string(StreamTemplate template) =>
        template.Format;
}
