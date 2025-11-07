namespace Kurrent.Client.Streams;

/// <summary>
/// Static factory for creating filter expressions from SQL-like text
/// </summary>
public static class FilterExpression {
    /// <summary>
    /// Creates a filter expression from a SQL-like query string
    /// </summary>
    /// <param name="expression">SQL-like expression string</param>
    /// <returns>Result containing the filter expression or error message</returns>
    public static FilterResult Create(string expression) {
        if (string.IsNullOrWhiteSpace(expression))
            return FilterResult.Empty;

        var parseResult = FilterParser.TryParse(expression.AsSpan());
        
        if (parseResult.IsSuccess && parseResult.Expression != null) {
            var filterExpression = new CompiledFilter(
                parseResult.Expression,
                parseResult.Expression.UsesStreamOrSchemaProperty()
            );
            return FilterResult.Success(filterExpression);
        }

        if (parseResult.IsEmpty)
            return FilterResult.Empty;

        return FilterResult.Failure(parseResult.Error ?? "Unknown parsing error");
    }

    /// <summary>
    /// Creates a filter expression from a SQL-like query span
    /// </summary>
    /// <param name="expression">SQL-like expression span</param>
    /// <returns>Result containing the filter expression or error message</returns>
    public static FilterResult Create(ReadOnlySpan<char> expression) {
        if (expression.IsWhiteSpace() || expression.IsEmpty)
            return FilterResult.Empty;

        var parseResult = FilterParser.TryParse(expression);
        
        if (parseResult.IsSuccess && parseResult.Expression != null) {
            var filterExpression = new CompiledFilter(
                parseResult.Expression,
                parseResult.Expression.UsesStreamOrSchemaProperty()
            );
            return FilterResult.Success(filterExpression);
        }

        if (parseResult.IsEmpty)
            return FilterResult.Empty;

        return FilterResult.Failure(parseResult.Error ?? "Unknown parsing error");
    }
}

/// <summary>
/// Result of filter expression creation
/// </summary>
public readonly record struct FilterResult {
    readonly CompiledFilter? _filter;
    readonly string? _error;
    readonly bool _isEmpty;

    FilterResult(CompiledFilter? filter, string? error, bool isEmpty) {
        _filter = filter;
        _error = error;
        _isEmpty = isEmpty;
    }

    /// <summary>
    /// Whether the filter creation was successful
    /// </summary>
    public bool IsSuccess => _filter != null && _error == null;

    /// <summary>
    /// Whether the expression was empty
    /// </summary>
    public bool IsEmpty => _isEmpty;

    /// <summary>
    /// Whether the filter creation failed
    /// </summary>
    public bool IsValid => IsSuccess;

    /// <summary>
    /// The created filter expression (only available if IsSuccess is true)
    /// </summary>
    public CompiledFilter Value => _filter ?? throw new InvalidOperationException("Filter result does not contain a valid filter");

    /// <summary>
    /// Error message (only available if IsSuccess is false)
    /// </summary>
    public string Error => _error ?? throw new InvalidOperationException("Filter result does not contain an error");

    /// <summary>
    /// Error messages (for compatibility)
    /// </summary>
    public string[] Errors => IsSuccess ? [] : [Error];

    /// <summary>
    /// Creates a successful result
    /// </summary>
    public static FilterResult Success(CompiledFilter filter) => new(filter, null, false);

    /// <summary>
    /// Creates a failure result
    /// </summary>
    public static FilterResult Failure(string error) => new(null, error, false);

    /// <summary>
    /// Creates an empty result
    /// </summary>
    public static FilterResult Empty => new(null, null, true);
}

/// <summary>
/// A compiled filter expression ready for evaluation
/// </summary>
public sealed record CompiledFilter(IFilterExpression Expression, bool IsStreamSpecific) {
    /// <summary>
    /// Whether this filter can be optimized for single stream reads
    /// </summary>
    public bool CanOptimizeForStream => IsStreamSpecific;

    /// <summary>
    /// Target stream name if this filter is optimized for a specific stream
    /// </summary>
    public string? TargetStream {
        get {
            if (!IsStreamSpecific) return null;
            
            // Try to extract stream name from simple equality expressions
            if (Expression is PropertyEqualsExpression { PropertyName: "stream" } equals)
                return equals.Value?.ToString();
                
            // For complex expressions, would need more sophisticated analysis
            return null;
        }
    }

    /// <summary>
    /// Evaluates the filter against a record
    /// </summary>
    /// <typeparam name="TRecord">Record type</typeparam>
    /// <param name="record">Record to evaluate</param>
    /// <param name="accessor">Record accessor for property access</param>
    /// <returns>True if the record matches the filter</returns>
    public bool Evaluate<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) {
        return Expression.Evaluate(record, accessor);
    }
}