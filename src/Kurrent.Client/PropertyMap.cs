using System.Collections.Frozen;

namespace Kurrent.Client;

/// <summary>
/// Strategy for parsing property paths into (name, key) candidates.
/// The implementation calls <c>tryResolve</c> for each candidate it produces;
/// returns <c>true</c> as soon as a candidate matches.
/// </summary>
[PublicAPI]
public interface IPathParser {
    /// <summary>
    /// Produces (name, key) candidates from <paramref name="path"/> and calls
    /// <paramref name="tryResolve"/> for each. Returns <c>true</c> when a candidate matches.
    /// </summary>
    bool TryParse(string path, Func<string, string?, bool> tryResolve);
}

/// <summary>
/// Built-in <see cref="IPathParser"/> implementations for <see cref="PropertyMap{T}"/>.
/// </summary>
[PublicAPI]
public static class PathParsers {
    /// <summary>
    /// DuckDB-compatible path parser. Supports all property access patterns that are valid DuckDB SQL:
    /// <list type="bullet">
    ///   <item>Bracket notation: <c>properties['region']</c>, <c>properties["region"]</c></item>
    ///   <item>Dot notation: <c>properties.region</c>, <c>data.address.city</c></item>
    /// </list>
    /// Bracket notation is tried first; dot notation splits right-to-left on <c>'.'</c>.
    /// <para>
    /// The <c>-&gt;</c> (single arrow) operator is not supported. It returns JSON type, which is only
    /// useful for chained traversal (<c>data-&gt;'a'-&gt;'b'</c>). Since <c>-&gt;&gt;</c> and dot
    /// notation cover all filter use cases, there is no reason to add it.
    /// </para>
    /// </summary>
    public static readonly IPathParser DuckDb = new DuckDbParser();

    sealed class DuckDbParser : IPathParser {
        public bool TryParse(string path, Func<string, string?, bool> tryResolve) {
            // bracket notation: name['key'] or name["key"]
            var bracket = path.IndexOf('[');
            if (bracket > 0) {
                var name  = path[..bracket];
                var inner = path.AsSpan(bracket + 1);

                if (inner.Length >= 3 && inner[^1] == ']') {
                    inner = inner[..^1];
                    if (inner[0] is '\'' or '"' && inner[^1] == inner[0])
                        inner = inner[1..^1];

                    if (tryResolve(name, inner.ToString()))
                        return true;
                }
            }

            // dot notation: split right-to-left on '.'
            for (var i = path.Length - 1; i >= 0; i--) {
                if (path[i] == '.' && tryResolve(path[..i], path[(i + 1)..]))
                    return true;
            }

            return false;
        }
    }
}

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
public sealed class PropertyMap<T> {
    readonly FrozenDictionary<string, IPropertyAccessor<T>> _accessors;
    readonly IPathParser? _pathParser;

    readonly FrozenDictionary<string, IPropertyAccessor<T>>.AlternateLookup<ReadOnlySpan<char>> _spanLookup;

    PropertyMap(FrozenDictionary<string, IPropertyAccessor<T>> accessors, IPathParser? pathParser) {
        _accessors  = accessors;
        _pathParser = pathParser;

        _spanLookup = accessors.GetAlternateLookup<ReadOnlySpan<char>>();
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
    /// Looks up the accessor for <paramref name="name"/> and returns a <see cref="PropertyResult{T}"/>
    /// that can be probed for typed values without boxing. Supports dot-path fallback and trim.
    /// </summary>
    public PropertyResult<T> Find(string name, T instance, string? trimPrefix = null, string? trimSuffix = null) =>
        Find(name, null, instance, trimPrefix, trimSuffix);

    /// <summary>
    /// Looks up the accessor for <paramref name="name"/> with optional <paramref name="key"/> and
    /// returns a <see cref="PropertyResult{T}"/> that can be probed for typed values without boxing.
    /// When a keyed accessor is registered and a key is provided, the key is passed through.
    /// When no key is provided, splits <paramref name="name"/> right-to-left on <c>'.'</c> to find
    /// a registered prefix with the trailing segment as key (dot-path fallback).
    /// When <paramref name="trimPrefix"/> or <paramref name="trimSuffix"/> is provided, retries
    /// the full resolution with the affix stripped if nothing matched.
    /// </summary>
    public PropertyResult<T> Find(string name, string? key, T instance, string? trimPrefix = null, string? trimSuffix = null) {
        if (_accessors.TryGetValue(name, out var accessor))
            return WrapResult(accessor, instance, key);

        // path parser fallback: delegate produces (name, key) candidates
        if (key is null && _pathParser is not null) {
            IPropertyAccessor<T>? matched = null;
            string? matchedKey = null;

            if (_pathParser.TryParse(name, (candidateName, candidateKey) => {
                if (!_accessors.TryGetValue(candidateName, out var acc)) return false;
                matched    = acc;
                matchedKey = candidateKey;
                return true;
            }) && matched is not null) {
                return WrapResult(matched, instance, matchedKey);
            }
        }

        // trim fallback: strip prefix/suffix and retry without trim
        var trimmed = TrimName(name, trimPrefix, trimSuffix);
        if (trimmed is not null)
            return Find(trimmed, key, instance);

        return default;
    }

    /// <summary>
    /// Resolves a value using the registered accessors with dot-path and trim fallback.
    /// Convenience wrapper over <see cref="Find(string, string?, T, string?, string?)"/>
    /// that returns the boxed value directly.
    /// </summary>
    public object? Resolve(string name, string? key, T instance, string? trimPrefix = null, string? trimSuffix = null) =>
        Find(name, key, instance, trimPrefix, trimSuffix).Value;

    static PropertyResult<T> WrapResult(IPropertyAccessor<T> accessor, T instance, string? key) =>
        key is not null && accessor is IKeyedPropertyAccessor<T> keyed
            ? new(keyed, instance, key)
            : new PropertyResult<T>(accessor, instance);

    static string? TrimName(string name, string? prefix, string? suffix) {
        if (prefix is null && suffix is null) return null;

        ReadOnlySpan<char> span = name;

        if (prefix is not null && span.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            span = span[prefix.Length..];

        if (suffix is not null && span.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            span = span[..^suffix.Length];

        return span.Length == name.Length ? null : span.ToString();
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

    /// <summary>
    /// Mutable builder for registering property accessors before freezing into an immutable
    /// <see cref="PropertyMap{T}"/>. Supports fluent chaining via <see cref="Register{TValue}"/>.
    /// </summary>
    public sealed class Builder {
        readonly Dictionary<string, IPropertyAccessor<T>> _map = new(StringComparer.OrdinalIgnoreCase);

        IPathParser? _pathParser = PathParsers.DuckDb;

        /// <summary>
        /// Sets the path parsing strategy used by <see cref="PropertyMap{T}.Find(string, string?, T, string?, string?)"/>
        /// to derive (name, key) candidates from dot-paths or bracket notation.
        /// Defaults to <see cref="PathParsers.DuckDb"/>. Pass <c>null</c> to disable path parsing.
        /// </summary>
        public Builder UsePathParser(IPathParser? parser) {
            _pathParser = parser;
            return this;
        }

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

        public PropertyMap<T> Build() => new(_map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), _pathParser);
    }
}

/// <summary>
/// The result of a <see cref="PropertyMap{T}.Find(string, T)"/> lookup. A lightweight struct
/// that holds a reference to the accessor and the instance, allowing the caller to probe for
/// typed values without boxing via <see cref="Is{TValue}"/>.
/// </summary>
/// <typeparam name="T">The type whose properties are accessed.</typeparam>
public readonly struct PropertyResult<T> {
    readonly IPropertyAccessor<T>? _accessor;
    readonly T _instance;
    readonly string? _key;

    internal PropertyResult(IPropertyAccessor<T> accessor, T instance, string? key = null) {
        _accessor = accessor;
        _instance = instance;
        _key      = key;
    }

    /// <summary>
    /// Returns <c>true</c> if the lookup matched a registered accessor.
    /// </summary>
    public bool HasValue => _accessor is not null;

    /// <summary>
    /// Returns the property value as <c>object?</c>. May box value types.
    /// Returns <c>null</c> when <see cref="HasValue"/> is <c>false</c>.
    /// </summary>
    public object? Value =>
        _key is not null && _accessor is IKeyedPropertyAccessor<T> keyed
            ? keyed.Get(_instance, _key)
            : _accessor?.Get(_instance);

    /// <summary>
    /// Probes the accessor for a specific return type without boxing.
    /// Returns <c>true</c> and the typed value when the accessor's concrete type matches
    /// <typeparamref name="TValue"/>; otherwise returns <c>false</c>.
    /// </summary>
    public bool Is<TValue>(out TValue result) {
        if (_accessor is PropertyAccessor<T, TValue> typed) {
            result = typed.GetTyped(_instance);
            return true;
        }

        result = default!;
        return false;
    }
}

/// <summary>
/// Extension methods for <see cref="PropertyResult{T}"/> providing common type coercions.
/// </summary>
public static class PropertyResultExtensions {
    /// <summary>
    /// Attempts to extract a <see cref="decimal"/> value by probing for common numeric types
    /// (<c>decimal</c>, <c>long</c>, <c>int</c>, <c>double</c>) without boxing.
    /// </summary>
    public static bool TryGetDecimal<T>(this PropertyResult<T> prop, out decimal result) {
        if (prop.Is<decimal>(out result))              return true;
        if (prop.Is<long>(out var l))   { result = l;           return true; }
        if (prop.Is<int>(out var i))    { result = i;           return true; }
        if (prop.Is<double>(out var d)) { result = (decimal)d;  return true; }
        result = 0;
        return false;
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
