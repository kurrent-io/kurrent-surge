namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// Result of a write operation indicating how many records were inserted, updated, and deleted.
/// </summary>
/// <remarks>
/// <para>
/// Provides comprehensive visibility into complex write operations that may
/// perform multiple types of modifications in a single transaction.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var result = await stateStore
///     .Table&lt;Order&gt;("orders")
///     .Where(o =&gt; o.Status == "expired")
///     .DeleteAsync();
///
/// Console.WriteLine($"Total affected: {result.Total}");
/// </code>
/// </example>
/// <param name="Inserted">The number of records that were inserted.</param>
/// <param name="Updated">The number of records that were updated.</param>
/// <param name="Deleted">The number of records that were deleted.</param>
[PublicAPI]
public readonly record struct WriteResult(int Inserted, int Updated, int Deleted) {
    /// <summary>
    /// Gets the total number of records affected by the write operation.
    /// </summary>
    public int Total => Inserted + Updated + Deleted;

    /// <summary>
    /// Gets a value indicating whether any records were inserted.
    /// </summary>
    public bool HasInserts => Inserted > 0;

    /// <summary>
    /// Gets a value indicating whether any records were updated.
    /// </summary>
    public bool HasUpdates => Updated > 0;

    /// <summary>
    /// Gets a value indicating whether any records were deleted.
    /// </summary>
    public bool HasDeletes => Deleted > 0;

    /// <summary>
    /// Creates a <see cref="WriteResult"/> representing only inserts.
    /// </summary>
    /// <param name="count">The number of records inserted.</param>
    /// <returns>A <see cref="WriteResult"/> with only insert count set.</returns>
    public static WriteResult FromInserts(int count) => new(count, 0, 0);

    /// <summary>
    /// Creates a <see cref="WriteResult"/> representing only updates.
    /// </summary>
    /// <param name="count">The number of records updated.</param>
    /// <returns>A <see cref="WriteResult"/> with only update count set.</returns>
    public static WriteResult FromUpdates(int count) => new(0, count, 0);

    /// <summary>
    /// Creates a <see cref="WriteResult"/> representing only deletes.
    /// </summary>
    /// <param name="count">The number of records deleted.</param>
    /// <returns>A <see cref="WriteResult"/> with only delete count set.</returns>
    public static WriteResult FromDeletes(int count) => new(0, 0, count);

    /// <summary>
    /// Returns a string representation of this write result.
    /// </summary>
    /// <returns>A string describing the insert, update, and delete counts.</returns>
    public override string ToString() =>
        $"Inserted: {Inserted}, Updated: {Updated}, Deleted: {Deleted}";
}
