using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Client.Queries;

public sealed record ParsedQuery(
    IReadOnlyList<ProjectionField> Fields,
    IFilterExpression?             Where,
    int?                           Limit
);

public sealed record ProjectionField(string Path, string? Alias) {
    public string Name => Alias ?? Path;
}

public sealed class QueryParseException(string message, int position) : Exception(message) {
    public int Position { get; } = position;
}
