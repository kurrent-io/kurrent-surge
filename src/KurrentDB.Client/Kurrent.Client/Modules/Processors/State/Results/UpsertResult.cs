namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// Result of an upsert operation indicating how many records were inserted vs updated.
/// </summary>
/// <remarks>
/// <para>
/// Upsert operations either insert new records or update existing ones based on
/// the primary key. This result type provides visibility into which action was taken.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var result = await stateStore
///     .Table&lt;Order&gt;("orders")
///     .UpsertAsync(order);
///
/// Console.WriteLine($"Inserted: {result.Inserted}, Updated: {result.Updated}");
/// </code>
/// </example>
/// <param name="Inserted">The number of records that were inserted.</param>
/// <param name="Updated">The number of records that were updated.</param>
[PublicAPI]
public readonly record struct UpsertResult(int Inserted, int Updated) {
    /// <summary>
    /// Gets the total number of records affected by the upsert operation.
    /// </summary>
    public int Total => Inserted + Updated;

    /// <summary>
    /// Gets a value indicating whether any records were inserted.
    /// </summary>
    public bool HasInserts => Inserted > 0;

    /// <summary>
    /// Gets a value indicating whether any records were updated.
    /// </summary>
    public bool HasUpdates => Updated > 0;

    /// <summary>
    /// Returns a string representation of this upsert result.
    /// </summary>
    /// <returns>A string describing the insert and update counts.</returns>
    public override string ToString() => $"Inserted: {Inserted}, Updated: {Updated}";
}
