using System.Linq.Expressions;
using System.Numerics;

namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// Fluent builder for single-value state operations.
/// </summary>
/// <remarks>
/// <para>
/// Provides a composable API for reading, modifying, and deleting single values in the state store.
/// Operations are composed using pipeline methods and executed when a terminal method is called.
/// </para>
/// <para>
/// <b>Pipeline methods</b> (return builder for chaining):
/// <see cref="When"/>, <see cref="Where"/>, <see cref="Set(Action{T})"/>,
/// <see cref="Increment{TField}"/>, <see cref="Decrement{TField}"/>
/// </para>
/// <para>
/// <b>Terminal methods</b> (execute the pipeline):
/// <see cref="GetAsync"/>, <see cref="UpdateAsync(Action{T}, CancellationToken)"/>,
/// <see cref="StoreAsync(CancellationToken)"/>, <see cref="DeleteAsync(CancellationToken)"/>
/// </para>
/// </remarks>
/// <typeparam name="T">The type of value stored in state. Must be a reference type.</typeparam>
/// <example>
/// <code>
/// // Simple get
/// var stats = await state.Value&lt;OrderStats&gt;("stats").GetAsync();
///
/// // Fluent update with condition
/// await state
///     .Value&lt;OrderStats&gt;("stats")
///     .When(s =&gt; s?.Count &gt; 0)
///     .Increment(s =&gt; s.Count)
///     .StoreAsync();
///
/// // Update with action
/// await state
///     .Value&lt;OrderStats&gt;("stats")
///     .UpdateAsync(s =&gt; {
///         s.Count++;
///         s.Total += order.Amount;
///     });
/// </code>
/// </example>
[PublicAPI]
public interface IValueStateBuilder<T> where T : class {
    /// <summary>
    /// Gets the state key for this builder.
    /// </summary>
    StateKey Key { get; }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Conditions (pipeline composition)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds a condition that must be satisfied for write operations to proceed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The condition is evaluated against the current value before applying modifications.
    /// If the condition returns <c>false</c>, write operations are skipped.
    /// </para>
    /// </remarks>
    /// <param name="condition">A predicate that receives the current value (may be null).</param>
    /// <returns>This builder for method chaining.</returns>
    IValueStateBuilder<T> When(Func<T?, bool> condition);

    /// <summary>
    /// Adds a filter condition for write operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Alias for <see cref="When"/> for LINQ-style API consistency.
    /// </para>
    /// </remarks>
    /// <param name="condition">A predicate that receives the current value (may be null).</param>
    /// <returns>This builder for method chaining.</returns>
    IValueStateBuilder<T> Where(Func<T?, bool> condition);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Modifications (pipeline composition - accumulated until terminal)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds a modification action to apply to the value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Modifications are accumulated and applied in order when a terminal write method is called.
    /// The action receives the current value and can modify it in-place.
    /// </para>
    /// </remarks>
    /// <param name="modifier">An action that modifies the value.</param>
    /// <returns>This builder for method chaining.</returns>
    IValueStateBuilder<T> Set(Action<T> modifier);

    /// <summary>
    /// Sets a specific field to a value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses expression trees to identify the field to set.
    /// Modifications are accumulated and applied when a terminal write method is called.
    /// </para>
    /// </remarks>
    /// <typeparam name="TField">The type of the field.</typeparam>
    /// <param name="field">Expression identifying the field to set.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>This builder for method chaining.</returns>
    IValueStateBuilder<T> Set<TField>(Expression<Func<T, TField>> field, TField value);

    /// <summary>
    /// Increments a numeric field by the specified amount.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses expression trees to identify the field to increment.
    /// If <paramref name="amount"/> is not specified, increments by 1.
    /// </para>
    /// </remarks>
    /// <typeparam name="TField">The numeric type of the field.</typeparam>
    /// <param name="field">Expression identifying the numeric field to increment.</param>
    /// <param name="amount">The amount to add. Defaults to 1 if not specified.</param>
    /// <returns>This builder for method chaining.</returns>
    IValueStateBuilder<T> Increment<TField>(Expression<Func<T, TField>> field, TField amount = default!)
        where TField : INumber<TField>;

    /// <summary>
    /// Decrements a numeric field by the specified amount.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses expression trees to identify the field to decrement.
    /// If <paramref name="amount"/> is not specified, decrements by 1.
    /// </para>
    /// </remarks>
    /// <typeparam name="TField">The numeric type of the field.</typeparam>
    /// <param name="field">Expression identifying the numeric field to decrement.</param>
    /// <param name="amount">The amount to subtract. Defaults to 1 if not specified.</param>
    /// <returns>This builder for method chaining.</returns>
    IValueStateBuilder<T> Decrement<TField>(Expression<Func<T, TField>> field, TField amount = default!)
        where TField : INumber<TField>;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Terminal: Read
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Retrieves the current value from state.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The value if it exists; otherwise, <c>null</c>.</returns>
    ValueTask<T?> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves the current value from state, returning a default if not found.
    /// </summary>
    /// <param name="defaultValue">The value to return if the state entry does not exist.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The value if it exists; otherwise, <paramref name="defaultValue"/>.</returns>
    ValueTask<T> GetAsync(T defaultValue, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the current value from state, throwing if not found.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the state entry does not exist.</exception>
    ValueTask<T> GetRequiredAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves the current value or creates it using the provided factory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If the value does not exist, the factory is invoked to create a new value,
    /// which is stored before being returned.
    /// </para>
    /// </remarks>
    /// <param name="factory">A function that creates the value if it does not exist.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The existing or newly created value.</returns>
    ValueTask<T?> GetOrCreateAsync(Func<T> factory, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the current value or creates it using the provided async factory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If the value does not exist, the factory is invoked to create a new value,
    /// which is stored before being returned.
    /// </para>
    /// </remarks>
    /// <param name="factory">An async function that creates the value if it does not exist.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The existing or newly created value.</returns>
    ValueTask<T?> GetOrCreateAsync(Func<ValueTask<T>> factory, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the value exists in state.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns><c>true</c> if the value exists; otherwise, <c>false</c>.</returns>
    ValueTask<bool> ExistsAsync(CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Terminal: Write (executes pipeline: get → apply transforms → store)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Updates the value using the specified modifier action.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retrieves the current value, applies the modifier, and stores the result.
    /// If the value does not exist and a default can be created, it is created first.
    /// </para>
    /// </remarks>
    /// <param name="modifier">An action that modifies the value in-place.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask UpdateAsync(Action<T> modifier, CancellationToken ct = default);

    /// <summary>
    /// Updates the value using the specified transformer function.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retrieves the current value, applies the transformer, and stores the result.
    /// The transformer can return <c>null</c> to delete the value.
    /// </para>
    /// </remarks>
    /// <param name="transformer">A function that transforms the value.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask UpdateAsync(Func<T?, T?> transformer, CancellationToken ct = default);

    /// <summary>
    /// Stores the value with all accumulated modifications applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Executes the pipeline: retrieves the current value, applies all accumulated
    /// modifications from <see cref="Set(Action{T})"/>, <see cref="Increment{TField}"/>, etc.,
    /// checks conditions from <see cref="When"/>, and stores the result.
    /// </para>
    /// </remarks>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask StoreAsync(CancellationToken ct = default);

    /// <summary>
    /// Stores the specified value, replacing any existing value.
    /// </summary>
    /// <param name="value">The value to store.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask StoreAsync(T value, CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Terminal: Delete
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Deletes the value from state.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask DeleteAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes the value from state if the condition is satisfied.
    /// </summary>
    /// <param name="condition">A predicate that determines whether to delete.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask DeleteAsync(Func<T?, bool> condition, CancellationToken ct = default);
}
