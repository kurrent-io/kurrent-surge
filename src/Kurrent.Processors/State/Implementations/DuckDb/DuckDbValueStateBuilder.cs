using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;

namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// DuckDB-backed fluent builder for single-value state operations.
/// </summary>
/// <remarks>
/// <para>
/// Implements the pipeline pattern where conditions and modifications are accumulated
/// and executed when a terminal method is called.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of value stored in state. Must be a reference type.</typeparam>
internal sealed class DuckDbValueStateBuilder<T> : IValueStateBuilder<T> where T : class {
    readonly DuckDbStateStore    _store;
    readonly StateKey            _key;
    readonly List<Func<T?, bool>> _conditions    = [];
    readonly List<Action<T>>     _modifications = [];

    /// <summary>
    /// Creates a new value state builder.
    /// </summary>
    /// <param name="store">The backing state store.</param>
    /// <param name="key">The state key for this value.</param>
    internal DuckDbValueStateBuilder(DuckDbStateStore store, StateKey key) {
        _store = store;
        _key   = key;
    }

    /// <inheritdoc />
    public StateKey Key => _key;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Conditions (pipeline composition)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public IValueStateBuilder<T> When(Func<T?, bool> condition) {
        _conditions.Add(condition);
        return this;
    }

    /// <inheritdoc />
    public IValueStateBuilder<T> Where(Func<T?, bool> condition) => When(condition);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Modifications (pipeline composition)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public IValueStateBuilder<T> Set(Action<T> modifier) {
        _modifications.Add(modifier);
        return this;
    }

    /// <inheritdoc />
    public IValueStateBuilder<T> Set<TField>(Expression<Func<T, TField>> field, TField value) {
        var setter = CreateFieldSetter(field, value);
        _modifications.Add(setter);
        return this;
    }

    /// <inheritdoc />
    public IValueStateBuilder<T> Increment<TField>(Expression<Func<T, TField>> field, TField amount = default!)
        where TField : INumber<TField> {

        var getter = field.Compile();
        var setter = CreateFieldSetterFromExpression(field);

        // Default to 1 if amount is default
        var incrementBy = EqualityComparer<TField>.Default.Equals(amount, default!)
            ? TField.One
            : amount;

        _modifications.Add(obj => {
            var current = getter(obj);
            setter(obj, current + incrementBy);
        });

        return this;
    }

    /// <inheritdoc />
    public IValueStateBuilder<T> Decrement<TField>(Expression<Func<T, TField>> field, TField amount = default!)
        where TField : INumber<TField> {

        var getter = field.Compile();
        var setter = CreateFieldSetterFromExpression(field);

        // Default to 1 if amount is default
        var decrementBy = EqualityComparer<TField>.Default.Equals(amount, default!)
            ? TField.One
            : amount;

        _modifications.Add(obj => {
            var current = getter(obj);
            setter(obj, current - decrementBy);
        });

        return this;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Terminal: Read
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async ValueTask<T?> GetAsync(CancellationToken ct = default) {
        await _store.EnsureInitializedAsync(ct).ConfigureAwait(false);
        return await _store.LoadValueAsync<T>(_key, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<T> GetAsync(T defaultValue, CancellationToken ct = default) {
        await _store.EnsureInitializedAsync(ct).ConfigureAwait(false);
        return await _store.LoadValueAsync<T>(_key, ct).ConfigureAwait(false) ?? defaultValue;
    }

    /// <inheritdoc />
    public async ValueTask<T> GetRequiredAsync(CancellationToken ct = default) {
        await _store.EnsureInitializedAsync(ct).ConfigureAwait(false);
        var value = await _store.LoadValueAsync<T>(_key, ct).ConfigureAwait(false);

        return value ?? throw new InvalidOperationException($"State entry '{_key}' does not exist.");
    }

    /// <inheritdoc />
    public async ValueTask<T?> GetOrCreateAsync(Func<T> factory, CancellationToken ct = default) {
        await _store.EnsureInitializedAsync(ct).ConfigureAwait(false);
        var existing = await _store.LoadValueAsync<T>(_key, ct).ConfigureAwait(false);

        if (existing is not null)
            return existing;

        var created = factory();
        await _store.SaveValueAsync(_key, created, ct).ConfigureAwait(false);
        return created;
    }

    /// <inheritdoc />
    public async ValueTask<T?> GetOrCreateAsync(Func<ValueTask<T>> factory, CancellationToken ct = default) {
        await _store.EnsureInitializedAsync(ct).ConfigureAwait(false);
        var existing = await _store.LoadValueAsync<T>(_key, ct).ConfigureAwait(false);

        if (existing is not null)
            return existing;

        var created = await factory().ConfigureAwait(false);
        await _store.SaveValueAsync(_key, created, ct).ConfigureAwait(false);
        return created;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(CancellationToken ct = default) {
        await _store.EnsureInitializedAsync(ct).ConfigureAwait(false);
        return await _store.ValueExistsAsync(_key, ct).ConfigureAwait(false);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Terminal: Write
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async ValueTask UpdateAsync(Action<T> modifier, CancellationToken ct = default) {
        await _store.EnsureInitializedAsync(ct).ConfigureAwait(false);
        var current = await _store.LoadValueAsync<T>(_key, ct).ConfigureAwait(false);

        // Create new instance if null (requires parameterless constructor)
        current ??= CreateInstance();

        modifier(current);

        await _store.SaveValueAsync(_key, current, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask UpdateAsync(Func<T?, T?> transformer, CancellationToken ct = default) {
        await _store.EnsureInitializedAsync(ct).ConfigureAwait(false);
        var current     = await _store.LoadValueAsync<T>(_key, ct).ConfigureAwait(false);
        var transformed = transformer(current);

        if (transformed is null) {
            await _store.DeleteValueAsync(_key, ct).ConfigureAwait(false);
        }
        else {
            await _store.SaveValueAsync(_key, transformed, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask StoreAsync(CancellationToken ct = default) {
        await _store.EnsureInitializedAsync(ct).ConfigureAwait(false);
        var current = await _store.LoadValueAsync<T>(_key, ct).ConfigureAwait(false);

        // Check all conditions
        if (!CheckConditions(current))
            return;

        // If no value and no modifications, nothing to do
        if (current is null && _modifications.Count == 0)
            return;

        // Create new instance if null (requires parameterless constructor)
        current ??= CreateInstance();

        // Apply all accumulated modifications
        ApplyModifications(current);

        // Save
        await _store.SaveValueAsync(_key, current, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask StoreAsync(T value, CancellationToken ct = default) {
        await _store.EnsureInitializedAsync(ct).ConfigureAwait(false);
        await _store.SaveValueAsync(_key, value, ct).ConfigureAwait(false);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Terminal: Delete
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async ValueTask DeleteAsync(CancellationToken ct = default) {
        await _store.EnsureInitializedAsync(ct).ConfigureAwait(false);
        await _store.DeleteValueAsync(_key, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(Func<T?, bool> condition, CancellationToken ct = default) {
        await _store.EnsureInitializedAsync(ct).ConfigureAwait(false);
        var current = await _store.LoadValueAsync<T>(_key, ct).ConfigureAwait(false);

        if (condition(current))
            await _store.DeleteValueAsync(_key, ct).ConfigureAwait(false);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Private Helpers
    // ═══════════════════════════════════════════════════════════════════════════════

    bool CheckConditions(T? value) {
        foreach (var condition in _conditions) {
            if (!condition(value))
                return false;
        }

        return true;
    }

    void ApplyModifications(T value) {
        foreach (var modification in _modifications) {
            modification(value);
        }
    }

    static Action<T> CreateFieldSetter<TField>(Expression<Func<T, TField>> fieldExpression, TField value) {
        var setter = CreateFieldSetterFromExpression(fieldExpression);
        return obj => setter(obj, value);
    }

    static Action<T, TField> CreateFieldSetterFromExpression<TField>(Expression<Func<T, TField>> fieldExpression) {
        if (fieldExpression.Body is not MemberExpression memberExpr)
            throw new ArgumentException("Expression must be a property or field access.", nameof(fieldExpression));

        var memberInfo = memberExpr.Member;

        return memberInfo switch {
            PropertyInfo prop when prop.CanWrite => (obj, val) => prop.SetValue(obj, val),
            FieldInfo field                      => (obj, val) => field.SetValue(obj, val),
            PropertyInfo                         => throw new ArgumentException($"Property '{memberInfo.Name}' is read-only.", nameof(fieldExpression)),
            _                                    => throw new ArgumentException($"Member '{memberInfo.Name}' is not a property or field.", nameof(fieldExpression))
        };
    }

    static T CreateInstance() {
        try {
            return Activator.CreateInstance<T>();
        }
        catch (MissingMethodException) {
            throw new InvalidOperationException(
                $"Type '{typeof(T).Name}' does not have a parameterless constructor. " +
                "Either provide an existing value or use a type with a parameterless constructor.");
        }
    }
}
