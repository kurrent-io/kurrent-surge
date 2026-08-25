using System.Globalization;

namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// Values bound to the <c>@name</c> placeholders in a filter expression. Each <c>Add</c> takes the
/// value at its own type and converts it once, so nothing is boxed and no type is guessed.
/// </summary>
/// <remarks>
/// A bound value never becomes text in the expression. It parses to a literal node directly, so a
/// value cannot change the shape of the expression it appears in.
/// </remarks>
[PublicAPI]
public sealed class FilterParameters {
    readonly Dictionary<string, FilterValue> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The names bound so far.
    /// </summary>
    public IReadOnlyCollection<string> Names => _values.Keys;

    public int Count => _values.Count;

    internal bool TryGet(string name, out FilterValue value) => _values.TryGetValue(name, out value!);

    FilterParameters Bind(string name, FilterValue value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // A repeated name is a mistake worth surfacing: the second binding would silently win, and the
        // expression reads as though both applied.
        if (!_values.TryAdd(Normalize(name), value))
            throw new ArgumentException($"Parameter '{name}' is already bound.", nameof(name));

        return this;

        // `@id` and `id` name the same parameter, so the leading marker is optional when binding.
        static string Normalize(string name) => name.StartsWith('@') ? name[1..] : name;
    }

    #region ->> Text <<-

    public FilterParameters Add(string name, string? value) =>
        Bind(name, value is null ? NullFilterValue.Instance : new StringFilterValue(value));

    public FilterParameters Add(string name, Guid value) =>
        Bind(name, new StringFilterValue(value.ToString("D")));

    public FilterParameters Add(string name, Enum value) =>
        Bind(name, new StringFilterValue(value.ToString()));

    #endregion

    #region ->> Numbers <<-

    public FilterParameters Add(string name, long value)    => Bind(name, new NumberFilterValue(value));
    public FilterParameters Add(string name, int value)     => Bind(name, new NumberFilterValue(value));
    public FilterParameters Add(string name, decimal value) => Bind(name, new NumberFilterValue(value));
    public FilterParameters Add(string name, double value)  => Bind(name, new NumberFilterValue((decimal)value));

    #endregion

    #region ->> Other <<-

    public FilterParameters Add(string name, bool value) =>
        Bind(name, new BooleanFilterValue(value));

    public FilterParameters Add(string name, byte[]? value) =>
        Bind(name, value is null ? NullFilterValue.Instance : new BlobFilterValue(value));

    /// <summary>
    /// Binds a moment as Unix milliseconds, which is what <c>created_at</c> holds.
    /// </summary>
    public FilterParameters Add(string name, DateTimeOffset value) =>
        Bind(name, new NumberFilterValue(value.ToUnixTimeMilliseconds()));

    /// <inheritdoc cref="Add(string,DateTimeOffset)"/>
    public FilterParameters Add(string name, DateTime value) =>
        Bind(name, new NumberFilterValue(new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds()));

    /// <summary>
    /// Binds a moment as a <c>TIMESTAMP</c> literal, for the calls that take one.
    /// </summary>
    public FilterParameters AddTimestamp(string name, DateTimeOffset value) =>
        Bind(name, new DateTimeFilterValue(value.UtcDateTime, "TIMESTAMP"));

    /// <summary>
    /// Binds an explicit SQL NULL. Every typed overload also accepts null where the type allows it.
    /// </summary>
    public FilterParameters AddNull(string name) =>
        Bind(name, NullFilterValue.Instance);

    #endregion

    public override string ToString() =>
        Count == 0 ? "(no parameters)" : string.Join(", ", _values.Select(p => $"@{p.Key}"));

    internal string Describe(string name) =>
        _values.TryGetValue(name, out var value)
            ? value switch {
                StringFilterValue s   => $"'{s.Value}'",
                NumberFilterValue n   => n.Value.ToString(CultureInfo.InvariantCulture),
                BooleanFilterValue b  => b.Value ? "TRUE" : "FALSE",
                BlobFilterValue blob  => $"from_hex('{Convert.ToHexString(blob.Value)}')",
                NullFilterValue       => "NULL",
                _                     => value.ToString() ?? "?"
            }
            : "?";
}
