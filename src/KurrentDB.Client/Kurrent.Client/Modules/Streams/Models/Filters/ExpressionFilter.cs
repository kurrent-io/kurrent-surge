using System.Text.RegularExpressions;
using static System.String;

namespace Kurrent.Client.Streams;

/// <summary>
/// A filter that matches records using SQL-like filter expressions.
/// </summary>
[PublicAPI]
public record ExpressionFilter : IRecordFilter {
    readonly Regex? _serverRegex;
    readonly IFilterExpression? _clientExpression;

    public StreamName TargetStream { get; }

    public string Expression { get; }

    internal string? ServerRegex { get; }

    internal bool IsStreamScope { get; }

    /// <summary>
    /// Creates a filter from a SQL-like expression.
    /// The expression is analyzed to extract server-side and client-side components.
    /// </summary>
    /// <param name="expression">A SQL-like filter expression (e.g., "stream = 'orders' AND properties.amount > 100")</param>
    public ExpressionFilter(string expression) {
        if (IsNullOrWhiteSpace(expression))
            throw new ArgumentNullException(nameof(expression));

        Expression = expression;

        var parser = new ExpressionFilterParser();
        var parseResult = parser.Parse(expression);

        if (!parseResult.IsSuccess)
            throw new ArgumentException($"Invalid SQL filter expression: {parseResult.Error}", nameof(expression));

        var analysis = ExpressionFilterParser.Analyze(parseResult.Value);

        _clientExpression = analysis.HasClientFilter ? analysis.ClientExpression : null;

        var hasTargetStream = analysis.TargetStream.HasValue;
        TargetStream = analysis.TargetStream ?? StreamName.None;

        if (hasTargetStream) {
            ServerRegex   = null;
            IsStreamScope = false;
            _serverRegex  = null;
        } else {
            ServerRegex   = analysis.ServerRegex;
            IsStreamScope = analysis.ServerScope == FilterScope.StreamName;
            _serverRegex  = IsNullOrEmpty(ServerRegex) ? null : new Regex(ServerRegex, RegexOptions.Compiled);
        }
    }

    public bool IsMatch(Record record) {
        if (TargetStream != StreamName.None && record.Stream != TargetStream)
            return false;

        if (_serverRegex is not null) {
            var value = IsStreamScope
                ? record.Stream.Value
                : record.Schema.SchemaName.Value;

            if (!_serverRegex.IsMatch(value))
                return false;
        }

        return _clientExpression?.IsMatch(record) ?? true;
    }
}

public sealed record LogExpressionFilter : ExpressionFilter {
    public LogExpressionFilter(string expression) : base(expression) { }
}

public sealed record StreamExpressionFilter : ExpressionFilter {
    public StreamExpressionFilter(string expression) : base(expression) {
        // ensure that the filter does not target any specific stream

    }
}
