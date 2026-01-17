using System.Linq.Expressions;
using System.Numerics;

namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// Fluent builder for operations on a specific entry in a map state.
/// </summary>
/// <remarks>
/// <para>
/// Provides a composable API for reading, modifying, and deleting a single entry in a map.
/// Similar to <see cref="IValueStateBuilder{T}"/> but for map entries.
/// </para>
/// </remarks>
/// <typeparam name="TKey">The type of the map key.</typeparam>
/// <typeparam name="TValue">The type of the map value. Must be a reference type.</typeparam>
/// <example>
/// <code>
/// // Get or create entry
/// var customer = await state
///     .Map&lt;string, CustomerStats&gt;("customers")
///     .Entry(customerId)
///     .GetOrPutAsync(() =&gt; new CustomerStats { CustomerId = customerId });
///
/// // Update entry
/// await state
///     .Map&lt;string, CustomerStats&gt;("customers")
///     .Entry(customerId)
///     .UpdateAsync(c =&gt; c.OrderCount++);
/// </code>
/// </example>
[PublicAPI]
public interface IMapEntryBuilder<TKey, TValue>
    where TKey : notnull
    where TValue : class {

    /// <summary>
    /// Gets the key of the entry being operated on.
    /// </summary>
    TKey EntryKey { get; }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Conditions
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds a condition that must be satisfied for write operations to proceed.
    /// </summary>
    /// <param name="condition">A predicate that receives the current value (may be null).</param>
    /// <returns>This builder for method chaining.</returns>
    IMapEntryBuilder<TKey, TValue> When(Func<TValue?, bool> condition);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Modifications
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds a modification action to apply to the entry value.
    /// </summary>
    /// <param name="modifier">An action that modifies the value.</param>
    /// <returns>This builder for method chaining.</returns>
    IMapEntryBuilder<TKey, TValue> Set(Action<TValue> modifier);

    /// <summary>
    /// Sets a specific field to a value.
    /// </summary>
    /// <typeparam name="TField">The type of the field.</typeparam>
    /// <param name="field">Expression identifying the field to set.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>This builder for method chaining.</returns>
    IMapEntryBuilder<TKey, TValue> Set<TField>(Expression<Func<TValue, TField>> field, TField value);

    /// <summary>
    /// Increments a numeric field by the specified amount.
    /// </summary>
    /// <typeparam name="TField">The numeric type of the field.</typeparam>
    /// <param name="field">Expression identifying the numeric field to increment.</param>
    /// <param name="amount">The amount to add. Defaults to 1 if not specified.</param>
    /// <returns>This builder for method chaining.</returns>
    IMapEntryBuilder<TKey, TValue> Increment<TField>(Expression<Func<TValue, TField>> field, TField amount = default!)
        where TField : INumber<TField>;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Terminal: Read
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Retrieves the entry value.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The value if the entry exists; otherwise, <c>null</c>.</returns>
    ValueTask<TValue?> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves the entry value, returning a default if not found.
    /// </summary>
    /// <param name="defaultValue">The value to return if the entry does not exist.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The value if it exists; otherwise, <paramref name="defaultValue"/>.</returns>
    ValueTask<TValue> GetAsync(TValue defaultValue, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the entry value, throwing if not found.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the entry does not exist.</exception>
    ValueTask<TValue> GetRequiredAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves the entry value or creates it using the provided factory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If the entry does not exist, the factory is invoked to create a new value,
    /// which is stored before being returned.
    /// </para>
    /// </remarks>
    /// <param name="factory">A function that creates the value if it does not exist.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The existing or newly created value.</returns>
    ValueTask<TValue?> GetOrPutAsync(Func<TValue> factory, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the entry exists in the map.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns><c>true</c> if the entry exists; otherwise, <c>false</c>.</returns>
    ValueTask<bool> ContainsAsync(CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Terminal: Write
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds or updates the entry with the specified value.
    /// </summary>
    /// <param name="value">The value to store.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask PutAsync(TValue value, CancellationToken ct = default);

    /// <summary>
    /// Adds the entry only if it does not already exist.
    /// </summary>
    /// <param name="value">The value to store if the entry is absent.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns><c>true</c> if the value was added; <c>false</c> if the entry already existed.</returns>
    ValueTask<bool> PutIfAbsentAsync(TValue value, CancellationToken ct = default);

    /// <summary>
    /// Updates the entry using the specified modifier action.
    /// </summary>
    /// <param name="modifier">An action that modifies the value in-place.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask UpdateAsync(Action<TValue> modifier, CancellationToken ct = default);

    /// <summary>
    /// Stores the entry with all accumulated modifications applied.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask StoreAsync(CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Terminal: Delete
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Removes the entry from the map and returns its value.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The removed value, or <c>null</c> if the entry did not exist.</returns>
    ValueTask<TValue?> RemoveAsync(CancellationToken ct = default);
}
