namespace Transfermarkt.Domain;

/// <summary>
/// A football season defined by its date range.
/// </summary>
/// <remarks>
/// Example: 2024/25 season runs from 2024-07-01 to 2025-06-30.
/// </remarks>
[PublicAPI]
public readonly record struct Season : IComparable<Season>, IComparable {
    Season(string name, DateOnly startDate, DateOnly endDate) {
        Name      = name;
        StartDate = startDate;
        EndDate   = endDate;
    }

    /// <summary>
    /// The display name of the season (e.g., "2024/25").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The start date of the season.
    /// </summary>
    public DateOnly StartDate { get; }

    /// <summary>
    /// The end date of the season.
    /// </summary>
    public DateOnly EndDate { get; }

    /// <summary>
    /// Checks if a date falls within this season.
    /// </summary>
    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;

    /// <summary>
    /// Creates a standard season starting in July of the given year.
    /// </summary>
    public static Season Create(int startYear) => new(
        $"{startYear}/{(startYear + 1) % 100:D2}",
        new DateOnly(startYear, 7, 1),
        new DateOnly(startYear + 1, 6, 30)
    );

    /// <summary>
    /// Creates a season from explicit values.
    /// </summary>
    public static Season From(string name, DateOnly startDate, DateOnly endDate) => new(name, startDate, endDate);

    public override string ToString() => Name;

    #region ->> relational members <<-

    public int CompareTo(Season other) => StartDate.CompareTo(other.StartDate);

    public int CompareTo(object? obj) =>
        obj switch {
            null         => 1,
            Season other => CompareTo(other),
            _            => throw new ArgumentException($"Object must be of type {nameof(Season)}")
        };

    public static bool operator <(Season left, Season right)  => left.CompareTo(right) < 0;
    public static bool operator >(Season left, Season right)  => left.CompareTo(right) > 0;
    public static bool operator <=(Season left, Season right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Season left, Season right) => left.CompareTo(right) >= 0;

    #endregion
}
