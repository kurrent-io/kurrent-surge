namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// Fluent builder for map state operations (key-value dictionary per state key).
/// </summary>
/// <remarks>
/// <para>
/// Provides a composable API for managing key-value pairs within a named map in the state store.
/// Use <see cref="Entry"/> to operate on individual entries or bulk methods for collection operations.
/// </para>
/// <para>
/// Map state is useful for scenarios like:
/// <list type="bullet">
/// <item>Customer-specific state indexed by customer ID</item>
/// <item>Session data indexed by session ID</item>
/// <item>Aggregations grouped by a key</item>
/// </list>
/// </para>
/// </remarks>
/// <typeparam name="TKey">The type of keys in the map. Must not be null.</typeparam>
/// <typeparam name="TValue">The type of values in the map. Must be a reference type.</typeparam>
/// <example>
/// <code>
/// // Single entry operations
/// await state
///     .Map&lt;string, CustomerStats&gt;("customers")
///     .Entry(customerId)
///     .UpdateAsync(c =&gt; c.OrderCount++);
///
/// // Bulk iteration
/// await foreach (var entry in state.Map&lt;string, CustomerStats&gt;("customers").EntriesAsync()) {
///     Console.WriteLine($"{entry.Key}: {entry.Value.OrderCount} orders");
/// }
/// </code>
/// </example>
[PublicAPI]
public interface IMapStateBuilder<TKey, TValue>
    where TKey : notnull
    where TValue : class {

    /// <summary>
    /// Gets the state key for this map.
    /// </summary>
    StateKey Key { get; }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Entry selection
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a builder for operating on a specific entry in the map.
    /// </summary>
    /// <param name="key">The key of the entry to operate on.</param>
    /// <returns>An <see cref="IMapEntryBuilder{TKey,TValue}"/> for the specified entry.</returns>
    IMapEntryBuilder<TKey, TValue> Entry(TKey key);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Bulk filtering
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Filters the map entries based on the specified predicate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The filter is applied to subsequent bulk read and delete operations.
    /// </para>
    /// </remarks>
    /// <param name="predicate">A function that returns true for entries to include.</param>
    /// <returns>This builder for method chaining.</returns>
    IMapStateBuilder<TKey, TValue> Where(Func<TKey, TValue, bool> predicate);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Bulk read
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Asynchronously enumerates all keys in the map.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>An async enumerable of keys.</returns>
    IAsyncEnumerable<TKey> KeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Asynchronously enumerates all values in the map.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>An async enumerable of values.</returns>
    IAsyncEnumerable<TValue> ValuesAsync(CancellationToken ct = default);

    /// <summary>
    /// Asynchronously enumerates all entries in the map.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>An async enumerable of key-value pairs.</returns>
    IAsyncEnumerable<KeyValuePair<TKey, TValue>> EntriesAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks whether the map is empty.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns><c>true</c> if the map contains no entries; otherwise, <c>false</c>.</returns>
    ValueTask<bool> IsEmptyAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the number of entries in the map.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The count of entries in the map.</returns>
    ValueTask<long> CountAsync(CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Bulk write
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds or updates multiple entries in the map.
    /// </summary>
    /// <param name="entries">The entries to add or update.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask PutAllAsync(IEnumerable<KeyValuePair<TKey, TValue>> entries, CancellationToken ct = default);

    /// <summary>
    /// Deletes all entries that match the current filter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If <see cref="Where"/> was called, only matching entries are deleted.
    /// Otherwise, all entries are deleted.
    /// </para>
    /// </remarks>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The number of entries deleted.</returns>
    ValueTask<int> DeleteAsync(CancellationToken ct = default);

    /// <summary>
    /// Removes all entries from the map.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The number of entries removed.</returns>
    ValueTask<int> ClearAsync(CancellationToken ct = default);
}
