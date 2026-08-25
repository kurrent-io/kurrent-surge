namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// Reads a parsed expression and reports what it says about one column, as alternatives a caller can
/// act on. A sibling of the compiler and the renderer: three readers of one tree, none of them knowing
/// what the others do with it.
/// </summary>
/// <remarks>
/// What it reports always describes a superset of the expression. Reporting less would let a caller
/// select fewer records than the filter accepts, so a construct that cannot be widened safely reports
/// nothing at all.
/// </remarks>
static class ConstraintAnalyzer {
    public static FilterConstraint Analyze<T>(IFilterExpression expression, FilterSchema<T> schema, string column) {
        var declared = schema.Find(column, null, out var columnKey);
        if (declared is null || columnKey is not null) return FilterConstraint.None;

        var terms = new List<FilterTerm>();

        // An OR only narrows when every branch does. One branch that says nothing about this column
        // would make the alternatives miss the records that branch selects.
        if (expression is OrExpression) {
            foreach (var disjunct in Flatten(expression, or: true)) {
                if (!TryTerm(disjunct, declared, schema, out var term)) return FilterConstraint.None;

                terms.Add(term);
            }

            return new(terms, IsExact: true);
        }

        var conjuncts = Flatten(expression, or: false);

        foreach (var conjunct in conjuncts) {
            if (TryTerm(conjunct, declared, schema, out var term))
                terms.Add(term);
        }

        if (terms.Count == 0) return FilterConstraint.None;

        // A single conjunct that reduced IS the expression. Two or more means the terms are read as
        // alternatives while the expression itself is stricter, so a caller still has to trim.
        return new(terms, IsExact: conjuncts.Count == 1);
    }

    static List<IFilterExpression> Flatten(IFilterExpression expression, bool or) {
        var result = new List<IFilterExpression>();
        Walk(expression, result, or);
        return result;

        static void Walk(IFilterExpression node, List<IFilterExpression> result, bool or) {
            switch (node) {
                case OrExpression e when or:
                    Walk(e.Left, result, or);
                    Walk(e.Right, result, or);
                    return;

                case AndExpression e when !or:
                    Walk(e.Left, result, or);
                    Walk(e.Right, result, or);
                    return;

                default:
                    result.Add(node);
                    return;
            }
        }
    }

    // NOT, IS NULL, BETWEEN and every inequality are absent here on purpose: none of them widens into
    // a set of values.
    static bool TryTerm<T>(IFilterExpression expression, FilterColumn<T> declared, FilterSchema<T> schema, out FilterTerm term) {
        switch (expression) {
            case ComparisonExpression { Op: ComparisonOperator.Equal, Value: StringFilterValue literal } e
                when IsColumn(e.Property, declared, schema):
                term = new(FilterTermKind.Equals, [literal.Value], CaseInsensitive: false);
                return true;

            case LikeExpression { Negated: false } e when IsColumn(e.Property, declared, schema):
                term = new(FilterTermKind.Like, [e.Pattern], e.CaseInsensitive);
                return true;

            case InExpression { Negated: false } e
                when IsColumn(e.Property, declared, schema) && TryStrings(e.Values, out var values):
                term = new(FilterTermKind.AnyOf, values, CaseInsensitive: false);
                return true;

            default:
                term = default;
                return false;
        }

        static bool TryStrings(IReadOnlyList<FilterValue> values, out List<string> strings) {
            strings = new(values.Count);

            foreach (var value in values) {
                if (value is not StringFilterValue literal) {
                    strings = [];
                    return false;
                }

                strings.Add(literal.Value);
            }

            return strings.Count > 0;
        }
    }

    // Aliases share one declaration, so identity settles whether a reference names the same column —
    // which is what makes `rec.stream` and any alias resolve. A key never qualifies: `stream['x']`
    // reads a member, not the column. Neither does a transform: `lower(stream)` constrains the
    // lowercased value, and a store asked to match the raw column would miss rows.
    static bool IsColumn<T>(PropertyAccess property, FilterColumn<T> declared, FilterSchema<T> schema) =>
        property.IsBare
     && ReferenceEquals(schema.Find(property.PropertyName, property.Key, out var key), declared)
     && key is null;
}
