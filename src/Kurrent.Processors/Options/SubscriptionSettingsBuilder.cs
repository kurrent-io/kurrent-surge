namespace Kurrent.Client.Streams;

/// <summary>
/// Fluent builder for <see cref="SubscriptionSettings"/>.
/// </summary>
[PublicAPI]
public sealed class SubscriptionSettingsBuilder : OptionsBuilder<SubscriptionSettingsBuilder, SubscriptionSettings> {
    internal SubscriptionSettingsBuilder(SubscriptionSettings settings) : base(settings) { }

    /// <summary>
    /// Initializes a new instance with default settings.
    /// </summary>
    public SubscriptionSettingsBuilder() : base(new SubscriptionSettings()) { }

    /// <summary>
    /// Sets a SQL-like filter expression for server-side filtering.
    /// </summary>
    /// <param name="expression">Filter expression (e.g., <c>"schema_name = 'OrderPlaced'"</c>).</param>
    /// <returns>The builder instance for method chaining.</returns>
    public SubscriptionSettingsBuilder FilterExpression(string expression) {
        var filter = new ExpressionFilter(expression);
        return With(s => s with { FilterExpression = expression, Filter = filter });
    }

    /// <summary>
    /// Sets a programmatic filter for client-side filtering.
    /// </summary>
    /// <param name="filter">The filter to apply to records.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public SubscriptionSettingsBuilder Filter(IRecordFilter filter) =>
        With(s => s with { Filter = filter });

    /// <summary>
    /// Sets the path to extract partition key from records.
    /// </summary>
    /// <param name="selector">Path expression (e.g., <c>"stream_id"</c>, <c>"data.customer_id"</c>).</param>
    /// <returns>The builder instance for method chaining.</returns>
    public SubscriptionSettingsBuilder PartitionKeySelector(string selector) =>
        With(s => s with { PartitionKeySelector = selector });

    /// <summary>
    /// Sets the initial position for processing when no checkpoint exists.
    /// </summary>
    /// <param name="position">The initial position (Earliest or Latest).</param>
    /// <returns>The builder instance for method chaining.</returns>
    public SubscriptionSettingsBuilder InitialPosition(SubscriptionInitialPosition position) =>
        With(s => s with { InitialPosition = position });
}
