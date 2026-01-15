namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// Represents a key for accessing state in the state store.
/// </summary>
/// <remarks>
/// <para>
/// StateKey combines a partition identifier with a string key to provide
/// logical addressing for state entries. The partition is a logical grouping
/// concept - whether state is global or local depends on the backend implementation.
/// </para>
/// <para>
/// Supports implicit conversion from <see cref="string"/> (uses partition 0)
/// and from <c>(int, string)</c> tuples for convenient usage.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Implicit conversion from string (partition 0)
/// StateKey key1 = "my-state";
///
/// // Implicit conversion from tuple
/// StateKey key2 = (5, "partition-state");
///
/// // Explicit construction
/// var key3 = new StateKey(10, "explicit-key");
/// </code>
/// </example>
/// <param name="Partition">
/// The partition identifier for this key.
/// Partition 0 is typically used for non-partitioned state.
/// </param>
/// <param name="Key">
/// The string key that uniquely identifies the state within its partition.
/// </param>
[PublicAPI]
public readonly record struct StateKey(int Partition, string Key) {
    /// <summary>
    /// Implicitly converts a string to a <see cref="StateKey"/> with partition 0.
    /// </summary>
    /// <param name="key">The string key to convert.</param>
    /// <returns>A <see cref="StateKey"/> with the specified key and partition 0.</returns>
    public static implicit operator StateKey(string key) => new(0, key);

    /// <summary>
    /// Implicitly converts a tuple of (partition, key) to a <see cref="StateKey"/>.
    /// </summary>
    /// <param name="t">The tuple containing partition identifier and key.</param>
    /// <returns>A <see cref="StateKey"/> with the specified partition and key.</returns>
    public static implicit operator StateKey((int partition, string key) t) => new(t.partition, t.key);

    /// <summary>
    /// Returns a string representation of this key.
    /// </summary>
    /// <remarks>
    /// For partition 0, returns just the key. For other partitions,
    /// returns the format "partition:key".
    /// </remarks>
    /// <returns>A string representation of this state key.</returns>
    public override string ToString() => Partition == 0 ? Key : $"{Partition}:{Key}";
}
