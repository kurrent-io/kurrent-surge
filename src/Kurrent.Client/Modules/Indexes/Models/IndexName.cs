using System.Text.RegularExpressions;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Indexes;

[PublicAPI]
public partial record IndexName {
    public static readonly IndexName Unset = new("");

    IndexName(string value) => Value = value;

    public string Value { get; }

    public bool IsUnset => this == Unset;

    public override string ToString() => Value;

    public static IndexName From(string? indexName) {
        if (string.IsNullOrWhiteSpace(indexName))
            throw new InvalidValueException<IndexName>(indexName, "cannot be null or whitespace.");

        if (indexName.Length > 64)
            throw new InvalidValueException<IndexName>(indexName, "cannot exceed 64 characters.");

        if (!Regex().IsMatch(indexName))
            throw new InvalidValueException<IndexName>(indexName, "must start with an uppercase letter, can only contain uppercase letters, digits, and underscores, and must end with an uppercase letter or digit.");

        return new(indexName);
    }

    const string Pattern = "^[A-Z][A-Z0-9_]{1,61}[A-Z0-9]$";

    static readonly Regex IndexNameRegEx = Regex();
    
    [GeneratedRegex(Pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex Regex();
}