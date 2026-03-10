namespace Transfermarkt.Domain;

/// <summary>
/// Football position as a value object with display name, short code, and category.
/// </summary>
[PublicAPI]
public readonly record struct Position {
    public static readonly Position AttackingMidfield = new("AM", "Attacking Midfield", PositionCategory.Midfielder);
    public static readonly Position CentralMidfield   = new("CM", "Central Midfield", PositionCategory.Midfielder);
    public static readonly Position CentreBack        = new("CB", "Centre-Back", PositionCategory.Defender);
    public static readonly Position CentreForward     = new("CF", "Centre-Forward", PositionCategory.Forward);
    public static readonly Position DefensiveMidfield = new("DM", "Defensive Midfield", PositionCategory.Midfielder);
    public static readonly Position Goalkeeper        = new("GK", "Goalkeeper", PositionCategory.Goalkeeper);
    public static readonly Position LeftBack          = new("LB", "Left-Back", PositionCategory.Defender);
    public static readonly Position LeftCentreBack    = new("LCB", "Left Centre-Back", PositionCategory.Defender);
    public static readonly Position LeftMidfield      = new("LM", "Left Midfield", PositionCategory.Midfielder);
    public static readonly Position LeftWingBack      = new("LWB", "Left Wing-Back", PositionCategory.Defender);
    public static readonly Position LeftWinger        = new("LW", "Left Winger", PositionCategory.Forward);
    public static readonly Position RightBack         = new("RB", "Right-Back", PositionCategory.Defender);
    public static readonly Position RightCentreBack   = new("RCB", "Right Centre-Back", PositionCategory.Defender);
    public static readonly Position RightMidfield     = new("RM", "Right Midfield", PositionCategory.Midfielder);
    public static readonly Position RightWingBack     = new("RWB", "Right Wing-Back", PositionCategory.Defender);
    public static readonly Position RightWinger       = new("RW", "Right Winger", PositionCategory.Forward);
    public static readonly Position SecondStriker     = new("SS", "Second Striker", PositionCategory.Forward);
    public static readonly Position Striker           = new("ST", "Striker", PositionCategory.Forward);
    public static readonly Position Sweeper           = new("SW", "Sweeper", PositionCategory.Defender);

    /// <summary>
    /// Represents an unknown position.
    /// </summary>
    public static readonly Position Unknown = new("XX", "Unknown", PositionCategory.Unknown);

    /// <summary>
    /// All available positions.
    /// </summary>
    public static readonly IReadOnlyList<Position> All = [
        Goalkeeper,
        Sweeper, CentreBack, LeftCentreBack, RightCentreBack, LeftBack, RightBack, LeftWingBack, RightWingBack,
        DefensiveMidfield, CentralMidfield, LeftMidfield, RightMidfield, AttackingMidfield,
        LeftWinger, RightWinger, SecondStriker, CentreForward, Striker
    ];

    static readonly Dictionary<string, Position> ByCode = All.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

    public Position(string code, string name, PositionCategory category) {
        Code     = code;
        Name     = name;
        Category = category;
    }

    /// <summary>
    /// Position code (e.g., "GK", "CB", "CM", "ST").
    /// </summary>
    public string Code { get; init; }

    /// <summary>
    /// Full position name.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Position category.
    /// </summary>
    public PositionCategory Category { get; init; }

    /// <summary>
    /// Whether this is an unknown position.
    /// </summary>
    public bool IsUnknown => this == Unknown;

    /// <summary>
    /// Gets a position by its short code.
    /// </summary>
    public static Position? FromCode(string code) => ByCode.TryGetValue(code, out var position) ? position : null;

    /// <summary>
    /// Gets all positions in a category.
    /// </summary>
    public static IEnumerable<Position> InCategory(PositionCategory category) => All.Where(p => p.Category == category);

    public override string ToString() => Name;
}

/// <summary>
/// Broad position category.
/// </summary>
[PublicAPI]
public enum PositionCategory {
    Unknown,
    Goalkeeper,
    Defender,
    Midfielder,
    Forward
}
