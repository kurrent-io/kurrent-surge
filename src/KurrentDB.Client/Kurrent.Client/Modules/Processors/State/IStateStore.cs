namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// Entry point for state management operations in record processors.
/// </summary>
/// <remarks>
/// <para>
/// Provides access to three types of state inspired by Apache Flink's state model:
/// <list type="bullet">
/// <item><see cref="Value{T}"/> - Single value per key (most common)</item>
/// <item><see cref="Map{TKey,TValue}"/> - Key-value dictionary per state key</item>
/// <item><see cref="Table{T}"/> - SQL-like operations with JSON storage</item>
/// </list>
/// </para>
/// <para>
/// State keys support implicit conversion from strings (using partition 0) and from
/// <c>(int, string)</c> tuples for explicit partition specification.
/// </para>
/// <para>
/// <b>Global vs Local State:</b> The <see cref="StateKey"/> provides logical addressing.
/// Whether state is shared globally or local to each worker depends on the backend
/// implementation (e.g., MotherDuck for shared, local DuckDB for isolated).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// class OrderProcessor : IRecordProcessor {
///     public async ValueTask ProcessRecords(ProcessRecordsContext ctx) {
///         foreach (var record in ctx.Records) {
///             var order = record.GetData&lt;OrderPlaced&gt;();
///
///             // Direct value access (shortcut)
///             var stats = await ctx.State.GetValueAsync&lt;OrderStats&gt;("stats");
///
///             // Fluent value operations
///             await ctx.State
///                 .Value&lt;OrderStats&gt;("stats")
///                 .UpdateAsync(s =&gt; s.Count++);
///
///             // Map operations
///             await ctx.State
///                 .Map&lt;string, CustomerStats&gt;("customers")
///                 .Entry(order.CustomerId)
///                 .UpdateAsync(c =&gt; c.OrderCount++);
///
///             // Table operations
///             var topOrders = await ctx.State
///                 .Table&lt;Order&gt;("orders")
///                 .Where(o =&gt; o.CustomerId == order.CustomerId)
///                 .OrderByDescending(o =&gt; o.Amount)
///                 .Take(10)
///                 .ToArrayAsync();
///         }
///     }
/// }
/// </code>
/// </example>
[PublicAPI]
public interface IStateStore  {
    // ═══════════════════════════════════════════════════════════════════════════════
    // Direct access shortcuts
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Directly retrieves a value from state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shortcut for <c>Value&lt;T&gt;(key).GetAsync()</c>.
    /// Use for simple read operations where fluent API is not needed.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The type of value to retrieve.</typeparam>
    /// <param name="key">The state key. Supports implicit conversion from string.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The value if it exists; otherwise, <c>null</c>.</returns>
    ValueTask<T?> GetValueAsync<T>(StateKey key, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Directly retrieves a value from state, returning a default if not found.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shortcut for <c>Value&lt;T&gt;(key).GetAsync(defaultValue)</c>.
    /// Use for simple read operations where fluent API is not needed.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The type of value to retrieve.</typeparam>
    /// <param name="key">The state key. Supports implicit conversion from string.</param>
    /// <param name="defaultValue">The value to return if not found.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The value if it exists; otherwise, <paramref name="defaultValue"/>.</returns>
    ValueTask<T> GetValueAsync<T>(StateKey key, T defaultValue, CancellationToken ct = default) where T : class;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Fluent builders
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a fluent builder for single-value state operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Value state is appropriate for storing a single object per key, such as:
    /// <list type="bullet">
    /// <item>Aggregation results</item>
    /// <item>Configuration settings</item>
    /// <item>Session state</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The type of value to store.</typeparam>
    /// <param name="key">The state key. Supports implicit conversion from string.</param>
    /// <returns>A <see cref="IValueStateBuilder{T}"/> for fluent operations.</returns>
    IValueStateBuilder<T> Value<T>(StateKey key) where T : class;

    /// <summary>
    /// Returns a fluent builder for map state operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Map state is appropriate for storing key-value pairs per state key, such as:
    /// <list type="bullet">
    /// <item>Customer data indexed by customer ID</item>
    /// <item>Session data indexed by session ID</item>
    /// <item>Lookup tables</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <typeparam name="TKey">The type of map keys. Must not be null.</typeparam>
    /// <typeparam name="TValue">The type of map values. Must be a reference type.</typeparam>
    /// <param name="key">The state key. Supports implicit conversion from string.</param>
    /// <returns>An <see cref="IMapStateBuilder{TKey,TValue}"/> for fluent operations.</returns>
    IMapStateBuilder<TKey, TValue> Map<TKey, TValue>(StateKey key) where TKey : notnull where TValue : class;

    /// <summary>
    /// Returns a fluent builder for SQL-like table operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Table state is appropriate for complex querying and aggregation, such as:
    /// <list type="bullet">
    /// <item>Event logs with filtering and ordering</item>
    /// <item>Analytics data with aggregations</item>
    /// <item>Complex queries across multiple fields</item>
    /// </list>
    /// </para>
    /// <para>
    /// Data is stored as JSON and can be indexed for performance.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type. Must be a reference type.</typeparam>
    /// <param name="key">The state key. Supports implicit conversion from string.</param>
    /// <returns>An <see cref="ITableStateBuilder{T}"/> for fluent operations.</returns>
    ITableStateBuilder<T> Table<T>(StateKey key) where T : class;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Global operations
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Clears all state from the store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Warning:</b> This operation is destructive and cannot be undone.
    /// Use with caution.
    /// </para>
    /// </remarks>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask ClearAllAsync(CancellationToken ct = default);
}
