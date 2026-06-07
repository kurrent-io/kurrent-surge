// ReSharper disable InconsistentNaming

using System.Text.RegularExpressions;

namespace Kurrent.Client.Streams;

/// <summary>
/// Extracts server-side filter components from parsed filter expressions.
/// Stream filters have priority over schema filters to avoid scanning the full $all log.
/// </summary>
static class ServerFilterExtractor {
    /// <summary>
    /// Extracts the server-side filter from the parsed expression and reports whether the SDK must
    /// still evaluate the expression client-side. Priority: Stream (1) > Category (2) > Index (3) >
    /// Schema (4). <c>RequiresClientEvaluation</c> is <c>false</c> only when the server filter fully
    /// represents the expression (a stream-predicate union, or a single extractable predicate); a
    /// residual conjunct or no derivable filter makes it <c>true</c>.
    /// </summary>
    public static (RecordFilter ServerFilter, bool RequiresClientEvaluation) Extract(IFilterExpression expression) {
        // OR of pure stream predicates → a single regex union. Only valid when EVERY disjunct
        // is a stream predicate: the server filter must match a SUPERSET of the expression
        // (client-side evaluation trims the rest), and a stream regex cannot cover a disjunct
        // on another property (e.g. "stream = 'a' OR schema = 'b'") — those fall through to
        // the conjunct path below and, ultimately, to no server filter. A matched union consumes
        // the WHOLE expression, so nothing is left for the client.
        if (TryExtractStreamUnionFilter(expression, out var unionFilter))
            return (unionFilter, RequiresClientEvaluation: false);

        var conjuncts = FlattenAnd(expression);

        // One conjunct that maps to a server filter IS the whole expression. Two or more means only
        // one was lifted to the server and the rest must be refined client-side.
        var requiresClientEvaluation = conjuncts.Count > 1;

        // Priority 1: Stream filters (avoids full $all log scan)
        if (TryExtractStreamFilter(conjuncts, out var streamFilter))
            return (streamFilter, requiresClientEvaluation);

        // Priority 2: Category filters (targets $idx-ce-* secondary index streams)
        if (TryExtractCategoryFilter(conjuncts, out var categoryFilter))
            return (categoryFilter, requiresClientEvaluation);

        // Priority 3: Index filters (targets $idx-user-* user-defined index streams)
        if (TryExtractUserIndexFilter(conjuncts, out var indexFilter))
            return (indexFilter, requiresClientEvaluation);

        // Priority 4: Schema filters
        if (TryExtractSchemaFilter(conjuncts, out var schemaFilter))
            return (schemaFilter, requiresClientEvaluation);

        // No server filter possible — the entire expression runs client-side.
        return (RecordFilter.None, RequiresClientEvaluation: true);

        static List<IFilterExpression> FlattenAnd(IFilterExpression expr) {
            var result = new List<IFilterExpression>();
            FlattenAndRecursive(expr, result);
            return result;
        
            static void FlattenAndRecursive(IFilterExpression expr, List<IFilterExpression> result) {
                if (expr is AndExpression and) {
                    FlattenAndRecursive(and.Left, result);
                    FlattenAndRecursive(and.Right, result);
                }
                else
                    result.Add(expr);
            }
        }
    }

    #region ->> Stream Filter Extraction <<-

    static bool TryExtractStreamUnionFilter(IFilterExpression expression, out RecordFilter filter) {
        if (expression is not OrExpression or) {
            filter = RecordFilter.None;
            return false;
        }

        var disjuncts = new List<IFilterExpression>();
        FlattenOr(or, disjuncts);

        var regexPatterns   = new List<string>();
        var caseInsensitive = false;

        foreach (var disjunct in disjuncts) {
            // One non-stream disjunct poisons the union — the regex would under-match.
            if (!TryExtractStreamPredicate(disjunct, out var regex, out var ignoreCase, out _)) {
                filter = RecordFilter.None;
                return false;
            }

            regexPatterns.Add(regex);
            caseInsensitive |= ignoreCase;
        }

        var combined     = $"({string.Join("|", regexPatterns)})";
        var finalPattern = caseInsensitive ? $"(?i){combined}" : combined;

        filter = StreamRegexFilter.Parse(finalPattern);
        return true;

        static void FlattenOr(IFilterExpression expr, List<IFilterExpression> result) {
            if (expr is OrExpression nested) {
                FlattenOr(nested.Left, result);
                FlattenOr(nested.Right, result);
            }
            else
                result.Add(expr);
        }
    }

    static bool TryExtractStreamFilter(List<IFilterExpression> expressions, out RecordFilter filter) {
        var regexPatterns   = new List<string>();
        var exactStreams    = new List<string>();
        var caseInsensitive = false;

        foreach (var expr in expressions) {
            if (TryExtractStreamPredicate(expr, out var regex, out var ignoreCase, out var exactStream)) {
                regexPatterns.Add(regex);
                caseInsensitive |= ignoreCase;

                if (exactStream is not null)
                    exactStreams.Add(exactStream);
            }
        }

        if (regexPatterns.Count == 0) {
            filter = RecordFilter.None;
            return false;
        }

        // single exact match with no case-insensitivity → direct stream read
        if (exactStreams.Count == 1 && regexPatterns.Count == 1 && !caseInsensitive) {
            filter = StreamNameFilter.Parse(exactStreams[0]);
            return true;
        }

        var combinedRegex = CombineRegexPatterns(regexPatterns);
        var finalPattern  = caseInsensitive ? $"(?i){combinedRegex}" : combinedRegex;

        filter = StreamRegexFilter.Parse(finalPattern);
        return true;

        static string CombineRegexPatterns(List<string> patterns) =>
            patterns.Count == 1
                ? patterns[0]
                : $"({string.Join("|", patterns)})";
    }

    static bool TryExtractStreamPredicate(IFilterExpression expr, out string regex, out bool caseInsensitive, out string? exactStream) {
        switch (expr) {
            case ComparisonExpression { Op: ComparisonOperator.Equal, Value: StringFilterValue sv } comp
                when IsStreamProperty(comp.Property.PropertyName):
                regex           = $"^{Regex.Escape(sv.Value)}$";
                caseInsensitive = false;
                exactStream     = sv.Value;
                return true;

            case LikeExpression { Negated: false } like
                when IsStreamProperty(like.Property.PropertyName):
                regex           = ConvertLikePatternToRegex(like.Pattern);
                caseInsensitive = like.CaseInsensitive;
                exactStream     = null;
                return true;

            case InExpression { Negated: false } inExpr
                when IsStreamProperty(inExpr.Property.PropertyName) && TryExtractStringValues(inExpr.Values, out var values):
                var escaped = values.Select(Regex.Escape);
                regex           = $"^({string.Join("|", escaped)})$";
                caseInsensitive = false;
                exactStream     = null;
                return true;

            default:
                regex           = "";
                caseInsensitive = false;
                exactStream     = null;
                return false;
        }
        
        static bool IsStreamProperty(string propertyName) =>
            propertyName.Equals("stream", StringComparison.OrdinalIgnoreCase);
        
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
        
            static bool IsRegexMetaChar(char ch) =>
                ch is '\\' or '*' or '+' or '?' or '|' or '{' or '}' or '[' or ']' or '(' or ')' or '^' or '$' or '.';
        }
    }
    
    #endregion

    #region ->> Category Filter Extraction <<-

    static bool TryExtractCategoryFilter(List<IFilterExpression> expressions, out RecordFilter filter) {
        foreach (var expr in expressions) {
            if (expr is ComparisonExpression { Op: ComparisonOperator.Equal, Value: StringFilterValue sv } comp
                && IsCategoryProperty(comp.Property.PropertyName)) {
                filter = CategoryFilter.Parse(sv.Value);
                return true;
            }
        }

        filter = RecordFilter.None;
        return false;
        
        static bool IsCategoryProperty(string propertyName) =>
            propertyName.Equals("category", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region ->> Index Filter Extraction <<-

    static bool TryExtractUserIndexFilter(List<IFilterExpression> expressions, out RecordFilter filter) {
        foreach (var expr in expressions) {
            if (expr is IndexExpression { IndexName: var indexName }) {
                filter = UserIndexFilter.Parse(indexName);
                return true;
            }
        }

        filter = RecordFilter.None;
        return false;
    }

    #endregion

    #region ->> Schema Filter Extraction <<-

    static bool TryExtractSchemaFilter(List<IFilterExpression> expressions, out RecordFilter filter) {
        foreach (var expr in expressions) {
            if (expr is ComparisonExpression { Op: ComparisonOperator.Equal, Value: StringFilterValue sv } comp
                && IsSchemaProperty(comp.Property.PropertyName)) {
                filter = SchemaFilter.Parse(sv.Value);
                return true;
            }
        }

        filter = RecordFilter.None;
        return false;
        
        static bool IsSchemaProperty(string propertyName) =>
            propertyName.Equals("schema_name", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Equals("schema", StringComparison.OrdinalIgnoreCase);
    }
    
    #endregion
}
