// ReSharper disable InconsistentNaming

using System.Text.RegularExpressions;

namespace Kurrent.Client.Streams;

/// <summary>
/// Extracts server-side filter components from parsed filter expressions.
/// Stream filters have priority over schema filters to avoid scanning the full $all log.
/// </summary>
internal static class ServerFilterExtractor {
    /// <summary>
    /// Extracts a server-side filter from the parsed expression.
    /// Priority: Stream (1) > Category (2) > Index (3) > Schema (4).
    /// </summary>
    public static IRecordFilter Extract(IFilterExpression expression) {
        var conjuncts = FlattenAnd(expression);

        // Priority 1: Stream filters (avoids full $all log scan)
        if (TryExtractStreamFilter(conjuncts, out var streamFilter))
            return streamFilter;

        // Priority 2: Category filters (targets $idx-ce-* secondary index streams)
        if (TryExtractCategoryFilter(conjuncts, out var categoryFilter))
            return categoryFilter;

        // Priority 3: Index filters (targets $idx-user-* user-defined index streams)
        if (TryExtractIndexFilter(conjuncts, out var indexFilter))
            return indexFilter;

        // Priority 4: Schema filters
        if (TryExtractSchemaFilter(conjuncts, out var schemaFilter))
            return schemaFilter;

        // No server filter possible
        return RecordFilter.None;
    }

    #region ->> Stream Filter Extraction <<-

    static bool TryExtractStreamFilter(List<IFilterExpression> conjuncts, out IRecordFilter filter) {
        var regexPatterns   = new List<string>();
        var caseInsensitive = false;

        foreach (var conjunct in conjuncts) {
            if (TryExtractStreamPredicate(conjunct, out var regex, out var ignoreCase)) {
                regexPatterns.Add(regex);
                caseInsensitive |= ignoreCase;
            }
        }

        if (regexPatterns.Count == 0) {
            filter = RecordFilter.None;
            return false;
        }

        var combinedRegex = CombineRegexPatterns(regexPatterns);
        var finalPattern  = caseInsensitive ? $"(?i){combinedRegex}" : combinedRegex;
        var compiledRegex = new Regex(finalPattern, RegexOptions.Compiled);

        // StreamFilter auto-derives TargetStream from the pattern
        filter = new StreamFilter(compiledRegex);

        return true;
    }

    static bool TryExtractStreamPredicate(IFilterExpression expr, out string regex, out bool caseInsensitive) {
        switch (expr) {
            case ComparisonExpression { Op: ComparisonOperator.Equal, Value: StringFilterValue sv } comp
                when IsStreamProperty(comp.Property.PropertyName):
                regex           = $"^{Regex.Escape(sv.Value)}$";
                caseInsensitive = false;
                return true;

            case LikeExpression { Negated: false } like
                when IsStreamProperty(like.Property.PropertyName):
                regex           = ConvertLikePatternToRegex(like.Pattern);
                caseInsensitive = like.CaseInsensitive;
                return true;

            case InExpression { Negated: false } inExpr
                when IsStreamProperty(inExpr.Property.PropertyName) && TryExtractStringValues(inExpr.Values, out var values):
                var escaped = values.Select(Regex.Escape);
                regex           = $"^({string.Join("|", escaped)})$";
                caseInsensitive = false;
                return true;

            default:
                regex           = string.Empty;
                caseInsensitive = false;
                return false;
        }
    }

    static bool IsStreamProperty(string propertyName) =>
        propertyName.Equals("stream", StringComparison.OrdinalIgnoreCase);

    #endregion

    #region ->> Category Filter Extraction <<-

    static bool TryExtractCategoryFilter(List<IFilterExpression> conjuncts, out IRecordFilter filter) {
        foreach (var conjunct in conjuncts) {
            if (conjunct is ComparisonExpression { Op: ComparisonOperator.Equal, Value: StringFilterValue sv } comp
                && IsCategoryProperty(comp.Property.PropertyName)) {
                filter = new StreamFilter([$"$idx-ce-{sv.Value}"]);
                return true;
            }
        }

        filter = RecordFilter.None;
        return false;
    }

    static bool IsCategoryProperty(string propertyName) =>
        propertyName.Equals("category", StringComparison.OrdinalIgnoreCase);

    #endregion

    #region ->> Index Filter Extraction <<-

    static bool TryExtractIndexFilter(List<IFilterExpression> conjuncts, out IRecordFilter filter) {
        foreach (var conjunct in conjuncts) {
            if (conjunct is IndexExpression { IndexName: var name }) {
                filter = new StreamFilter([$"$idx-user-{name}"]);
                return true;
            }
        }

        filter = RecordFilter.None;
        return false;
    }

    #endregion

    #region ->> Schema Filter Extraction <<-

    static bool TryExtractSchemaFilter(List<IFilterExpression> conjuncts, out IRecordFilter filter) {
        foreach (var conjunct in conjuncts) {
            if (conjunct is ComparisonExpression { Op: ComparisonOperator.Equal, Value: StringFilterValue sv } comp
                && IsSchemaProperty(comp.Property.PropertyName)) {
                filter = new StreamFilter([$"$idx-et-{sv.Value}"]);
                return true;
            }
        }

        filter = RecordFilter.None;
        return false;
    }

    static bool IsSchemaProperty(string propertyName) =>
        propertyName.Equals("schema_name", StringComparison.OrdinalIgnoreCase)
        || propertyName.Equals("schema", StringComparison.OrdinalIgnoreCase);

    #endregion

    #region ->> Helpers <<-

    static List<IFilterExpression> FlattenAnd(IFilterExpression expr) {
        var result = new List<IFilterExpression>();
        FlattenAndRecursive(expr, result);
        return result;
    }

    static void FlattenAndRecursive(IFilterExpression expr, List<IFilterExpression> result) {
        if (expr is AndExpression and) {
            FlattenAndRecursive(and.Left, result);
            FlattenAndRecursive(and.Right, result);
        }
        else
            result.Add(expr);
    }

    static string CombineRegexPatterns(List<string> patterns) =>
        patterns.Count == 1
            ? patterns[0]
            : $"({string.Join("|", patterns)})";

    static string ConvertLikePatternToRegex(string pattern) {
        var result = new System.Text.StringBuilder(pattern.Length + 4);
        result.Append('^');

        foreach (var ch in pattern) {
            if (ch == '%')
                result.Append(".*");
            else if (ch == '_')
                result.Append('.');
            else if (IsRegexMetaChar(ch)) {
                result.Append('\\');
                result.Append(ch);
            }
            else
                result.Append(ch);
        }

        result.Append('$');
        return result.ToString();
    }

    static bool IsRegexMetaChar(char ch) =>
        ch is '\\' or '*' or '+' or '?' or '|' or '{' or '}' or '[' or ']' or '(' or ')' or '^' or '$' or '.';

    static bool TryExtractStringValues(IReadOnlyList<FilterValue> values, out List<string> strings) {
        strings = [];

        foreach (var v in values) {
            if (v is not StringFilterValue sv) {
                strings = [];
                return false;
            }

            strings.Add(sv.Value);
        }

        return strings.Count > 0;
    }

    #endregion
}
