using Kurrent.Surge.RecordFilters;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Chooses the wire filter that best narrows a read, and reports whether the client must still trim
/// what comes back.
/// </summary>
/// <remarks>
/// Every fact here is about the wire: which columns a server can be narrowed by, in what order, and
/// which of its three filter shapes carries the answer. The filter engine only says what an expression
/// constrains a column to. This whole file disappears once the wire carries the expression as SQL.
/// </remarks>
static class ServerFilterExtractor {
    public static (RecordFilter ServerFilter, bool RequiresClientEvaluation) Extract(Filter<Record> filter) {
        // Priority. A named stream is read directly, so it beats an index scan; the index streams
        // follow in the order the server resolves them most cheaply.
        if (filter.ConstraintOn("stream") is { Any: true } stream)
            return (ToStreamFilter(stream), RequiresClientEvaluation: !stream.IsExact);

        if (SingleValue(filter.ConstraintOn("category")) is ({ } category, var categoryIsExact))
            return (CategoryFilter.Parse(category), RequiresClientEvaluation: !categoryIsExact);

        if (filter.ConstraintOn("schema_name") is { Any: true } schema)
            return (ToSchemaFilter(schema), RequiresClientEvaluation: !schema.IsExact);

        // Nothing the server can narrow by, but the request still carries a filter: the system-record
        // exclusion takes the wire slot and the whole expression runs on the client.
        return (RecordFilter.ExcludeSystemRecords, RequiresClientEvaluation: true);
    }

    // A stream is the one column all three wire shapes can carry. One case-sensitive literal reads the
    // stream directly and skips the $all scan entirely; anything else has to travel as a regex.
    static RecordFilter ToStreamFilter(FilterConstraint constraint) =>
        constraint.Terms is [{ Kind: FilterTermKind.Equals, CaseInsensitive: false } single]
            ? ToNamedStream(single.Values[0])
            : StreamRegexFilter.Parse(ToRegexUnion(constraint.Terms));

    // A schema is the event-type column. One case-sensitive literal names a single index stream and is
    // reached by prefix; anything else travels as an event-type regex, the shape a stream constraint
    // already uses on the other wire context.
    static RecordFilter ToSchemaFilter(FilterConstraint constraint) =>
        constraint.Terms is [{ Kind: FilterTermKind.Equals, CaseInsensitive: false } single]
            ? SchemaFilter.Parse(single.Values[0])
            : SchemaRegexFilter.Parse(ToRegexUnion(constraint.Terms));

    // An index stream named by value is still a stream predicate. The only difference is how it
    // travels: a secondary index is reached by prefix, an ordinary stream by a direct read.
    static RecordFilter ToNamedStream(string stream) =>
        stream switch {
            _ when stream.StartsWith(CategoryIndexPrefix,  StringComparison.Ordinal) => CategoryFilter.Parse(stream),
            _ when stream.StartsWith(SchemaIndexPrefix,    StringComparison.Ordinal) => SchemaFilter.Parse(stream),
            _ when stream.StartsWith(UserIndexPrefix,      StringComparison.Ordinal) => UserIndexFilter.Parse(stream),
            _                                                                        => StreamNameFilter.Parse(stream)
        };

    const string CategoryIndexPrefix = "$idx-ce-";
    const string SchemaIndexPrefix   = "$idx-et-";
    const string UserIndexPrefix     = "$idx-user-";

    // An index stream is reached by a prefix, and a prefix is one literal. Alternatives have no wire
    // form here, so anything but a single equality leaves the column unusable.
    static (string? Value, bool IsExact) SingleValue(FilterConstraint constraint) =>
        constraint.Terms is [{ Kind: FilterTermKind.Equals } single]
            ? (single.Values[0], constraint.IsExact)
            : (null, false);

    static string ToRegexUnion(IReadOnlyList<FilterTerm> terms) {
        var patterns        = new List<string>(terms.Count);
        var caseInsensitive = false;

        foreach (var term in terms) {
            patterns.Add(term.ToRegex());
            caseInsensitive |= term.CaseInsensitive;
        }

        var combined = patterns.Count == 1 ? patterns[0] : $"({string.Join("|", patterns)})";

        // Inline, because the pattern travels to the server as text and carries no options with it.
        return caseInsensitive ? $"(?i){combined}" : combined;
    }
}
