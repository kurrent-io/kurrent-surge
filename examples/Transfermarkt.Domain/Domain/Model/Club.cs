namespace Transfermarkt.Domain;

/// <summary>
/// Club reference data. Clubs don't have domain events - they're reference data.
/// </summary>
[PublicAPI]
public readonly record struct Club {
    Club(ClubId id, string name, string shortName, string country, string league) {
        Id        = id;
        Name      = name;
        ShortName = shortName;
        Country   = country;
        League    = league;
    }

    /// <summary>
    /// Unique club identifier.
    /// </summary>
    public ClubId Id { get; }

    /// <summary>
    /// Full club name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Short club name or abbreviation.
    /// </summary>
    public string ShortName { get; }

    /// <summary>
    /// Country where the club is based.
    /// </summary>
    public string Country { get; }

    /// <summary>
    /// League the club competes in.
    /// </summary>
    public string League { get; }

    /// <summary>
    /// Creates a Club from the specified values.
    /// </summary>
    public static Club From(ClubId id, string name, string shortName, string country, string league) =>
        new(id, name, shortName, country, league);

    public override string ToString() => $"{Name} ({ShortName})";
}
