using System.Collections.Frozen;

namespace Kurrent.Client;

/// <summary>
/// Immutable property map binding string aliases to typed property accessors over <typeparamref name="T"/>.
/// Built once via <see cref="Build"/> with a <see cref="Builder"/>, then frozen into a
/// <see cref="FrozenDictionary{TKey,TValue}"/> for O(1) case-insensitive lookup.
/// <para>
/// Each <see cref="Builder.Register{TValue}"/> call binds a single typed accessor to one or more alias names.
/// Duplicate aliases (case-insensitive) are rejected at build time with <see cref="ArgumentException"/>.
/// </para>
/// <para>
/// Two resolution paths are available:
/// <list type="bullet">
///   <item><see cref="GetValue(string, T)"/> — untyped, returns <c>object?</c> (may box value types).</item>
///   <item><see cref="GetValue{TValue}(string, T)"/> — typed, avoids boxing when <typeparamref name="TValue"/>
///     matches the registered accessor's return type.</item>
/// </list>
/// </para>
/// <para>
/// On NET9+, span-based overloads avoid string allocation when the caller already has a span.
/// </para>
/// </summary>
/// <typeparam name="T">The type whose properties are accessed.</typeparam>
internal sealed class PropertyMap<T> {
    readonly FrozenDictionary<string, IPropertyAccessor<T>> _accessors;

#if NET9_0_OR_GREATER
    readonly FrozenDictionary<string, IPropertyAccessor<T>>.AlternateLookup<ReadOnlySpan<char>> _spanLookup;
#endif

    PropertyMap(FrozenDictionary<string, IPropertyAccessor<T>> accessors) {
        _accessors = accessors;

#if NET9_0_OR_GREATER
        _spanLookup = accessors.GetAlternateLookup<ReadOnlySpan<char>>();
#endif
    }

    /// <summary>
    /// Builds a new property map by configuring a <see cref="Builder"/> with accessor registrations.
    /// The builder is discarded after this call; the returned map is immutable.
    /// </summary>
    /// <param name="configure">Action that registers accessors via <see cref="Builder.Register{TValue}"/>.</param>
    public static PropertyMap<T> Build(Action<Builder> configure) {
        var builder = new Builder();
        configure(builder);
        return builder.Build();
    }

    /// <summary>
    /// Returns the property value for the given name, or <c>null</c> if the name is not registered.
    /// May box value types.
    /// </summary>
    public object? GetValue(string name, T instance) =>
        _accessors.TryGetValue(name, out var accessor) ? accessor.Get(instance) : null;

    /// <summary>
    /// Returns the property value for the given name and key. If the accessor is keyed and a key is
    /// provided, the key is passed through. Otherwise falls back to the scalar path.
    /// Returns <c>null</c> if the name is not registered.
    /// </summary>
    public object? GetValue(string name, string? key, T instance) {
        if (!_accessors.TryGetValue(name, out var accessor))
            return null;

        return key is not null && accessor is IKeyedPropertyAccessor<T> keyed
            ? keyed.Get(instance, key)
            : accessor.Get(instance);
    }

    /// <summary>
    /// Returns the property value for the given name as <typeparamref name="TValue"/>, avoiding boxing
    /// when <typeparamref name="TValue"/> matches the registered accessor's return type.
    /// Falls back to the untyped path with a cast on type mismatch.
    /// Returns <c>default</c> if the name is not registered.
    /// </summary>
    public TValue? GetValue<TValue>(string name, T instance) {
        if (!_accessors.TryGetValue(name, out var accessor))
            return default;

        // fast path: exact type match, no boxing
        if (accessor is PropertyAccessor<T, TValue> typed)
            return typed.GetTyped(instance);

        // slow path: untyped get + cast
        return (TValue?)accessor.Get(instance);
    }

    /// <summary>
    /// Attempts to extract a <see cref="decimal"/> value without boxing by pattern-matching the
    /// accessor's concrete type. Returns <c>false</c> if the name is not registered or the accessor
    /// does not produce a numeric type.
    /// </summary>
    public bool TryGetDecimalValue(string name, T instance, out decimal result) {
        if (_accessors.TryGetValue(name, out var accessor)) {
            switch (accessor) {
                case PropertyAccessor<T, long> a:
                    result = a.GetTyped(instance);
                    return true;
                case PropertyAccessor<T, int> a:
                    result = a.GetTyped(instance);
                    return true;
                case PropertyAccessor<T, decimal> a:
                    result = a.GetTyped(instance);
                    return true;
                case PropertyAccessor<T, double> a:
                    result = (decimal)a.GetTyped(instance);
                    return true;
            }
        }

        result = 0;
        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if the name is a registered alias.
    /// </summary>
    public bool HasProperty(string name) =>
        _accessors.ContainsKey(name);

    /// <summary>
    /// The total number of registered aliases (not unique accessors).
    /// </summary>
    public int Count => _accessors.Count;

#if NET9_0_OR_GREATER
    /// <summary>
    /// Returns the property value for the given name (as span), or <c>null</c> if not registered.
    /// Avoids string allocation. May box value types.
    /// </summary>
    public object? GetValue(ReadOnlySpan<char> name, T instance) =>
        _spanLookup.TryGetValue(name, out var accessor) ? accessor.Get(instance) : null;

    /// <summary>
    /// Returns <c>true</c> if the name (as span) is a registered alias, avoiding string allocation.
    /// </summary>
    public bool HasProperty(ReadOnlySpan<char> name) =>
        _spanLookup.ContainsKey(name);
#endif

    /// <summary>
    /// Mutable builder for registering property accessors before freezing into an immutable
    /// <see cref="PropertyMap{T}"/>. Supports fluent chaining via <see cref="Register{TValue}"/>.
    /// </summary>
    internal sealed class Builder {
        readonly Dictionary<string, IPropertyAccessor<T>> _map = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers a typed property accessor under one or more alias names.
        /// The compiler infers <typeparamref name="TValue"/> from the accessor delegate.
        /// All aliases are case-insensitive. Duplicate aliases throw <see cref="ArgumentException"/>.
        /// </summary>
        /// <typeparam name="TValue">The return type of the accessor. Inferred by the compiler.</typeparam>
        /// <param name="accessor">The typed accessor delegate that extracts a value from <typeparamref name="T"/>.</param>
        /// <param name="aliases">One or more alias names for this accessor.</param>
        /// <returns>This builder for fluent chaining.</returns>
        public Builder Register<TValue>(Func<T, TValue> accessor, params string[] aliases) {
            var propertyAccessor = new PropertyAccessor<T, TValue>(accessor);

            foreach (var alias in aliases) {
                if (!_map.TryAdd(alias, propertyAccessor))
                    throw new ArgumentException($"Duplicate alias '{alias}' in PropertyMap<{typeof(T).Name}>.");
            }

            return this;
        }

        /// <summary>
        /// Registers a keyed property accessor under one or more alias names.
        /// Keyed accessors receive a string key at resolve time (e.g., for map-type columns).
        /// </summary>
        /// <typeparam name="TValue">The return type of the accessor. Inferred by the compiler.</typeparam>
        /// <param name="accessor">The keyed accessor delegate that extracts a value using both instance and key.</param>
        /// <param name="aliases">One or more alias names for this accessor.</param>
        /// <returns>This builder for fluent chaining.</returns>
        public Builder RegisterKeyed<TValue>(Func<T, string?, TValue> accessor, params string[] aliases) {
            var propertyAccessor = new KeyedPropertyAccessor<T, TValue>(accessor);

            foreach (var alias in aliases) {
                if (!_map.TryAdd(alias, propertyAccessor))
                    throw new ArgumentException($"Duplicate alias '{alias}' in PropertyMap<{typeof(T).Name}>.");
            }

            return this;
        }

        public PropertyMap<T> Build() => new(_map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Untyped accessor interface for storing heterogeneous accessors in a single dictionary.
/// </summary>
/// <typeparam name="T">The type whose properties are accessed.</typeparam>
interface IPropertyAccessor<in T> {
    /// <summary>
    /// Extracts the property value from the instance. May box value types.
    /// </summary>
    object? Get(T instance);
}

/// <summary>
/// Keyed accessor interface for properties that require a string key at resolve time
/// (e.g., map-type columns like <c>properties['region']</c>).
/// </summary>
/// <typeparam name="T">The type whose properties are accessed.</typeparam>
interface IKeyedPropertyAccessor<in T> : IPropertyAccessor<T> {
    /// <summary>
    /// Extracts the property value from the instance using the specified key.
    /// </summary>
    object? Get(T instance, string? key);
}

/// <summary>
/// Typed property accessor that holds a <see cref="Func{T, TValue}"/> delegate.
/// <see cref="GetTyped"/> returns the value without boxing.
/// <see cref="IPropertyAccessor{T}.Get"/> returns <c>object?</c> (boxes value types).
/// </summary>
/// <typeparam name="T">The type whose properties are accessed.</typeparam>
/// <typeparam name="TValue">The property's return type.</typeparam>
sealed class PropertyAccessor<T, TValue>(Func<T, TValue> getter) : IPropertyAccessor<T> {
    /// <inheritdoc/>
    public object? Get(T instance) => getter(instance);

    /// <summary>
    /// Returns the typed value without boxing.
    /// </summary>
    public TValue GetTyped(T instance) => getter(instance);
}

/// <summary>
/// Keyed property accessor that holds a <see cref="Func{T, String, TValue}"/> delegate.
/// <see cref="IPropertyAccessor{T}.Get(T)"/> calls the delegate with a <c>null</c> key.
/// <see cref="IKeyedPropertyAccessor{T}.Get(T, String)"/> passes the key through.
/// </summary>
sealed class KeyedPropertyAccessor<T, TValue>(Func<T, string?, TValue> getter) : IKeyedPropertyAccessor<T> {
    /// <inheritdoc/>
    public object? Get(T instance) => getter(instance, null);

    /// <inheritdoc/>
    public object? Get(T instance, string? key) => getter(instance, key);
}
