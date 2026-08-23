using System.Text.RegularExpressions;

namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// What an expression says about one column.
/// </summary>
/// <remarks>
/// Terms are alternatives: a value satisfying any one of them satisfies the column. The set always
/// describes a superset of the expression, so acting on it can over-select but never under-select.
/// </remarks>
/// <param name="Terms">The alternatives, empty when the expression constrains the column to nothing usable.</param>
/// <param name="IsExact">Whether the terms are the whole expression rather than a widening of it.</param>
public sealed record FilterConstraint(IReadOnlyList<FilterTerm> Terms, bool IsExact) {
    /// <summary>
    /// The expression says nothing usable about the column.
    /// </summary>
    public static readonly FilterConstraint None = new([], false);

    /// <summary>
    /// Whether the expression constrains the column at all.
    /// </summary>
    public bool Any => Terms.Count > 0;
}

/// <summary>
/// One alternative a column may satisfy.
/// </summary>
/// <param name="Kind">How <paramref name="Values"/> is read.</param>
/// <param name="Values">One value for <see cref="FilterTermKind.Equals"/> and
/// <see cref="FilterTermKind.Like"/>, one or more for <see cref="FilterTermKind.AnyOf"/>.</param>
/// <param name="CaseInsensitive">Whether the term was written case-insensitively.</param>
public readonly record struct FilterTerm(FilterTermKind Kind, IReadOnlyList<string> Values, bool CaseInsensitive) {
    /// <summary>
    /// The term as an anchored regular expression. LIKE translation lives here because the engine is
    /// what knows <c>%</c> and <c>_</c>; case-insensitivity is reported, not applied, so a caller
    /// combining several terms decides where the flag goes.
    /// </summary>
    public string ToRegex() =>
        Kind switch {
            FilterTermKind.Equals => $"^{Regex.Escape(Values[0])}$",
            FilterTermKind.AnyOf  => $"^({string.Join("|", Values.Select(Regex.Escape))})$",
            FilterTermKind.Like   => LikePattern.ToRegexPattern(Values[0]),
            _                     => throw new NotSupportedException($"Unhandled term kind '{Kind}'.")
        };
}

/// <summary>
/// How a <see cref="FilterTerm"/> is read.
/// </summary>
public enum FilterTermKind {
    /// <summary>
    /// The column equals the value.
    /// </summary>
    Equals,

    /// <summary>
    /// The column equals one of the values.
    /// </summary>
    AnyOf,

    /// <summary>
    /// The column matches the SQL LIKE pattern.
    /// </summary>
    Like
}
