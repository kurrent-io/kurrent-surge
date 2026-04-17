using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Kurrent.Client.Streams;

namespace Kurrent.Client;

/// <summary>
/// Represents a collection of metadata as key-value pairs with additional helper methods.
/// Note: This class is not thread-safe. Synchronization must be provided by the caller when accessed from multiple threads.
/// </summary>
[PublicAPI]
[DebuggerDisplay("{DebuggerDisplay(true)}")]
[JsonConverter(typeof(MetadataJsonConverter))]
public class Metadata : IEnumerable<KeyValuePair<string, object?>>, IEquatable<Metadata> {
	internal readonly Dictionary<string, object?> Dictionary;

	/// <summary>
	/// Initializes a new, empty instance of the Metadata class using ordinal string comparison.
	/// </summary>
	public Metadata() =>
		Dictionary = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
	/// Initializes a new instance of the Metadata class from a collection of key-value pairs.
	/// </summary>
	/// <param name="items">The collection of key-value pairs to copy from.</param>
    public Metadata(IEnumerable<KeyValuePair<string, object?>> items) =>
        Dictionary = items switch {
            Metadata meta => new(meta.Dictionary, StringComparer.OrdinalIgnoreCase),
            IReadOnlyDictionary<string, object?> dic => new(dic, StringComparer.OrdinalIgnoreCase),
            _ => items.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase)
        };

    public static Metadata New => new();

    public static Metadata Create(Dictionary<string, object?> items) => new(items);

    /// <summary>
	/// Locks this metadata instance, making it immutable. Direct mutations will throw an exception.
	/// Use CreateUnlockedCopy() method to create a mutable copy when modifications are needed.
	/// </summary>
	/// <returns>This metadata instance for method chaining.</returns>
	/// <example>
	/// <code>
	/// var metadata = new Metadata().With("key", "value").Lock();
	///
	/// // This will throw an InvalidOperationException
	/// metadata["key"] = "new value";
	///
	/// // Create an unlocked copy for modifications
	/// var unlocked = metadata.CreateUnlockedCopy();
	/// var modified = unlocked.With("key", "new value");
	/// </code>
	/// </example>
	public Metadata Lock() {
		IsLocked = true;
		return this;
	}

	/// <summary>
	/// Creates an unlocked copy of this metadata instance that can be modified.
	/// The returned copy will have IsLocked = false regardless of the current instance's lock state.
	/// </summary>
	/// <returns>A new unlocked metadata instance containing the same key-value pairs.</returns>
	/// <example>
	/// <code>
	/// var locked = metadata.Lock();
	/// var unlocked = locked.CreateUnlockedCopy();
	///
	/// unlocked.IsLocked;  // false
	/// unlocked.With("new", "value");  // works normally
	///
	/// // Fluent API chains
	/// var result = locked.CreateUnlockedCopy()
	///     .With("key", "value")
	///     .Lock();
	/// </code>
	/// </example>
	public Metadata CreateUnlockedCopy() => new(this);

    Metadata ThrowIfLocked() =>
        IsLocked ? throw new InvalidOperationException("Cannot modify locked metadata. Use CreateUnlockedCopy() to obtain a mutable copy.") : this;

    /// <summary>
    /// Replaces the current metadata with the specified collection of key-value pairs.
    /// </summary>
    /// <param name="items">A collection of key-value pairs to replace the current metadata.</param>
    /// <returns>The current instance of <see cref="Metadata"/> after resetting its contents.</returns>
    public Metadata Reset(IEnumerable<KeyValuePair<string, object?>> items) {
        ThrowIfLocked();

        foreach (var item in items)
            Dictionary[item.Key] = item.Value;

        return this;
    }

    /// <summary>
    /// Creates a new Metadata instance where all values are converted to their string representation
    /// using appropriate formatting for common types (ISO dates, etc.).
    /// </summary>
    /// <returns>A new Metadata instance with all values as formatted strings.</returns>
    public Metadata Stringify() {
        return new Metadata()
            .WithMany(this.Select(kvp => new KeyValuePair<string, object?>(kvp.Key, ConvertValueToString(kvp.Value))));

        static string? ConvertValueToString(object? value) =>
            value switch {
                null                          => null,
                DateTime dateTime             => dateTime.ToString("O"),
                DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O"),
                DateOnly dateOnly             => dateOnly.ToString("O"),
                TimeOnly timeOnly             => timeOnly.ToString("O"),
                TimeSpan timeSpan             => timeSpan.ToString("c"),
                Guid guid                     => guid.ToString("D"),
                int int32                     => int32.ToString(CultureInfo.InvariantCulture),
                long int64                    => int64.ToString(CultureInfo.InvariantCulture),
                decimal dec                   => dec.ToString(CultureInfo.InvariantCulture),
                double dbl                    => dbl.ToString(CultureInfo.InvariantCulture),
                float flt                     => flt.ToString(CultureInfo.InvariantCulture),
                byte b                        => b.ToString(CultureInfo.InvariantCulture),
                char c                        => c.ToString(CultureInfo.InvariantCulture),
                byte[] bytes                  => Convert.ToBase64String(bytes),
                Memory<byte> memory           => Convert.ToBase64String(memory.Span),
                ReadOnlyMemory<byte> memory   => Convert.ToBase64String(memory.Span),
                string str                    => str,
                IEnumerable enumerable        => string.Join(", ", enumerable.Cast<object?>().Select(ConvertValueToString)),
                _                             => value.ToString()
            };
    }

    #region ->> IDictionary <<-

	/// <summary>
	/// Gets the number of elements in the collection.
	/// </summary>
	public int Count => Dictionary.Count;

	/// <summary>
	/// Gets a value indicating whether this metadata instance is locked and cannot be modified directly.
	/// </summary>
	public bool IsLocked { get; private set; }

	/// <summary>
	/// Gets an collection that contains the keys in the dictionary.
	/// </summary>
	public ICollection<string> Keys => Dictionary.Keys;

	/// <summary>
	/// Gets an collection that contains the values in the dictionary.
	/// </summary>
	public ICollection<object?> Values => Dictionary.Values;

	/// <summary>
	/// Gets or sets the value associated with the specified key.
	/// </summary>
	/// <param name="key">The key to look up or set.</param>
	/// <returns>The value associated with the specified key.</returns>
	/// <exception cref="KeyNotFoundException">The key does not exist in the collection (when getting).</exception>
	/// <exception cref="InvalidOperationException">Thrown when attempting to set a value on a locked metadata instance.</exception>
	public object? this[string key] {
		get => Dictionary[key];
		set => ThrowIfLocked().Dictionary[key] = value;
    }

	/// <summary>
	/// Determines whether the dictionary contains the specified key.
	/// </summary>
	/// <param name="key">The key to locate.</param>
	/// <returns>true if the dictionary contains an element with the specified key; otherwise, false.</returns>
	public bool ContainsKey(string key) => Dictionary.ContainsKey(key);

	/// <summary>
	/// Removes the specified key from the dictionary.
	/// </summary>
	/// <param name="key">The key to remove.</param>
	/// <returns>true if the element is successfully removed; otherwise, false.</returns>
	/// <exception cref="InvalidOperationException">Thrown when attempting to remove from a locked metadata instance.</exception>
	public bool Remove(string key) =>
        ThrowIfLocked().Dictionary.Remove(key);

    /// <summary>
	/// Returns an enumerator that iterates through the collection.
	/// </summary>
	/// <returns>An enumerator that can be used to iterate through the collection.</returns>
	public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => Dictionary.GetEnumerator();

	/// <summary>
	/// Returns an enumerator that iterates through the collection.
	/// </summary>
	/// <returns>An enumerator that can be used to iterate through the collection.</returns>
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	#endregion

	#region ->> Value Retrieval Methods <<-

	/// <summary>
	/// Tries to get a typed value from the metadata, with automatic conversion from strings.
	/// </summary>
	/// <typeparam name="T">The type to get or convert to.</typeparam>
	/// <param name="key">The key to retrieve.</param>
	/// <param name="value">When this method returns, contains the value if found and successfully converted; otherwise, default(T).</param>
	/// <returns>true if the key was found and the value could be converted to type T; otherwise, false.</returns>
	public bool TryGetValue<T>(string key, out T? value) {
		// check if the key exists
		if (!Dictionary.TryGetValue(key, out var obj)) {
			value = default;
			return false;
		}

		switch (obj) {
			// direct type match (most common case)
			case T typedValue:
				value = typedValue;
				return true;

			case JsonValue jsonValue:
				value = jsonValue.TryGetValue<T>(out var jsonTypedValue) ? jsonTypedValue : default;
				return true;

			// handle null case
			case null:
				value = default;
				return typeof(T).IsClass || Nullable.GetUnderlyingType(typeof(T)) is not null;

			// If the object is a string, try to convert it to type T, unless T is string itself
			case string stringValue: {
				// if T is string, we can directly assign
				if (typeof(T) == typeof(string)) {
					value = (T)(object)stringValue;
					return true;
				}

				stringValue = stringValue.Trim();

				var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

				// handle basic types with TypeConverter
				var converter = TypeDescriptor.GetConverter(targetType);
				if (converter.CanConvertFrom(typeof(string)))
					try {
						value = (T)converter.ConvertFromInvariantString(stringValue)!;
						return true;
					}
					catch {
						value = default;
						return false;
					}

                // handle enum types
                if (targetType.IsEnum) {
                    try {
                        if(Enum.TryParse(targetType, stringValue, ignoreCase: true, out var enumValue)) {
                            value = (T)enumValue;
                            return true;
                        }

                        value = default;
                        return false;
                    }
                    catch {
                        value = default;
                        return false;
                    }
                }

				break;
			}
		}

		// if we got here, we couldn't convert
		value = default;
		return false;
	}

    /// <summary>
    /// Gets the value associated with the specified key as the specified type, or a default value if the key is not found.
    /// </summary>
    /// <typeparam name="T">The type to cast the value to.</typeparam>
    /// <param name="key">The key to locate.</param>
    /// <param name="defaultValue">The default value to return if the key is not found or if the value cannot be cast.</param>
    /// <returns>The value associated with the specified key cast to type T, or the default value if the key is not found or the value cannot be cast.</returns>
    public T? GetValueOrDefault<T>(string key, T? defaultValue = default) =>
        TryGetValue<T>(key, out var typedValue)
            ? typedValue
            : defaultValue;

    /// <summary>
    /// Gets the value associated with the specified key as the specified type.
    /// Throws a KeyNotFoundException if the key is not found or if the value is null.
    /// </summary>
    /// <typeparam name="T">The type to get or convert to.</typeparam>
    /// <param name="key">The key to retrieve.</param>
    /// <returns>
    /// The value associated with the specified key cast to type T.
    /// </returns>
    public T GetRequiredValue<T>(string key) =>
        TryGetValue<T>(key, out var typedValue) && typedValue is not null
            ? typedValue
            : throw new KeyNotFoundException($"Key '{key}' not found or value is null.");

	#endregion

	#region ->> Modification Methods <<-

	/// <summary>
	/// Evolves the value associated with the specified key by applying a transformation function.
	/// </summary>
	public Metadata EvolveValue<T, TE>(string key, Func<T?, TE> transform) {
		ThrowIfLocked();

		if (TryGetValue<T>(key, out var value))
			Dictionary[key] = transform(value);

		return this;
	}

	/// <summary>
	/// Adds or updates a key-value pair in the metadata.
	/// </summary>
	/// <typeparam name="T">The type of the value.</typeparam>
	/// <param name="key">The key to add or update.</param>
	/// <param name="value">The value to set.</param>
	/// <returns>This metadata instance for method chaining.</returns>
	/// <exception cref="InvalidOperationException">Thrown when attempting to modify a locked metadata instance.</exception>
	public Metadata With<T>(string key, T? value) {
		ThrowIfLocked();
		Dictionary[key] = value;
		return this;
	}

	/// <summary>
	/// Sets a single key-value pair in the metadata.
	/// </summary>
	/// <param name="key">The key to set.</param>
	/// <param name="value">The value to associate with the key.</param>
	/// <returns>This metadata instance for method chaining.</returns>
	/// <exception cref="InvalidOperationException">Thrown when attempting to modify a locked metadata instance.</exception>
	public Metadata With(string key, object? value) {
		ThrowIfLocked();
		Dictionary[key] = value;
		return this;
	}

	/// <summary>
	/// Updates the current metadata with additional or updated key-value pairs.
	/// </summary>
	/// <param name="items">A collection of key-value pairs to update the metadata with.</param>
	/// <returns>This metadata instance for method chaining.</returns>
	/// <exception cref="InvalidOperationException">Thrown when attempting to modify a locked metadata instance.</exception>
	public Metadata WithMany(IEnumerable<KeyValuePair<string, object?>> items) {
		ThrowIfLocked();

        foreach (var item in items)
            Dictionary[item.Key] = item.Value;

        return this;
	}

	#endregion

	/// <summary>
	/// Returns a string representation of the metadata suitable for debugging.
	/// </summary>
	public override string ToString() =>
		$"Metadata ({Count} items): {string.Join("\n", Dictionary.Select(p => $"  {p.Key} = {p.Value ?? "null"}"))}";

	/// <summary>
	/// Creates a debug view.
	/// </summary>
	string DebuggerDisplay(bool prettyPrint = false) =>
		!prettyPrint ? ToString() : $"Metadata:\n{string.Join("\n", Dictionary.Select(p => $"  {p.Key} = {p.Value ?? "null"} {p.Value?.GetType().Name}"))}";

    #region ->> Equality <<-

    public bool Equals(Metadata? other) {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (Dictionary.Count != other.Dictionary.Count)
            return false;

        foreach (var kvp in Dictionary) {
            if (!other.Dictionary.TryGetValue(kvp.Key, out var otherValue))
                return false;

            if (!Equals(kvp.Value, otherValue))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is Metadata other && Equals(other);

    public override int GetHashCode() {
        var hash = new HashCode();

        foreach (var kvp in Dictionary.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) {
            hash.Add(kvp.Key, StringComparer.OrdinalIgnoreCase);
            hash.Add(kvp.Value);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(Metadata? left, Metadata? right) => Equals(left, right);

    public static bool operator !=(Metadata? left, Metadata? right) => !Equals(left, right);

    #endregion

	public static implicit operator Metadata(Dictionary<string, object?> dictionary) => new(dictionary);
}
