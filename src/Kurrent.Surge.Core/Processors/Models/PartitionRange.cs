namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// Represents a contiguous range of partitions.
/// </summary>
/// <remarks>
/// <para>
/// Used for efficient specification of partition ranges in snapshot and restore operations.
/// Both <see cref="Start"/> and <see cref="End"/> are inclusive.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Single partition
/// var single = PartitionRange.Single(5);
///
/// // Range of partitions (0-9 inclusive)
/// var range = PartitionRange.FromTo(0, 9);
///
/// // Check if partition is in range
/// if (range.Contains(7)) { ... }
/// </code>
/// </example>
/// <param name="Start">The starting partition number (inclusive).</param>
/// <param name="End">The ending partition number (inclusive).</param>
[PublicAPI]
public readonly record struct PartitionRange(int Start, int End) {
    /// <summary>
    /// Creates a range containing only a single partition.
    /// </summary>
    /// <param name="partition">The partition number.</param>
    /// <returns>A <see cref="PartitionRange"/> containing only the specified partition.</returns>
    public static PartitionRange Single(int partition) => new(partition, partition);

    /// <summary>
    /// Creates a range from a starting partition to an ending partition (inclusive).
    /// </summary>
    /// <param name="start">The starting partition number (inclusive).</param>
    /// <param name="end">The ending partition number (inclusive).</param>
    /// <returns>A <see cref="PartitionRange"/> spanning from start to end.</returns>
    public static PartitionRange FromTo(int start, int end) => new(start, end);

    /// <summary>
    /// Determines whether this range contains the specified partition.
    /// </summary>
    /// <param name="partition">The partition number to check.</param>
    /// <returns><c>true</c> if the partition is within this range; otherwise, <c>false</c>.</returns>
    public bool Contains(int partition) => partition >= Start && partition <= End;

    /// <summary>
    /// Gets the total number of partitions in this range.
    /// </summary>
    public int Count => End - Start + 1;

    /// <summary>
    /// Returns a string representation of this partition range.
    /// </summary>
    /// <returns>A string in the format "[Start-End]" or "[Partition]" for single partitions.</returns>
    public override string ToString() => Start == End ? $"[{Start}]" : $"[{Start}-{End}]";
}
