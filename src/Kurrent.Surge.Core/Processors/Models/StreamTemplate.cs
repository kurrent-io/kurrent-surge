// ReSharper disable PartialTypeWithSinglePart

using System.Runtime.CompilerServices;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Processors.Models;

/// <summary>
/// The StreamTemplate class represents a format for constructing,
/// parsing, and managing stream identifiers.
/// This class provides functionality for handling stream templates
/// with optional category-based organization.
/// </summary>
[PublicAPI]
public partial record StreamTemplate {
    public StreamTemplate(string format, char categorySeparator = '/') {
        Format   = format;
        Category = Format.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    }

    public string Format   { get; }
    public string Category { get; }

    public bool HasCategory => Category.Length > 0;

    public StreamName GetStream(string id) =>
        FormattableStringFactory.Create(Format, id).ToString();

    public StreamName GetStream(Guid id) =>
        GetStream(id.ToString());

    public static implicit operator StreamTemplate(StreamName _) => new(_.ToString());
    public static implicit operator StreamTemplate(string   _) => new(_);
    public static implicit operator string(StreamTemplate   _) => _.Format;
}
