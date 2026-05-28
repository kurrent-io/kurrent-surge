using System.Collections.Immutable;

namespace Transfermarkt.Domain.Model;

/// <summary>
/// A player's name including first name, last name, and optional nicknames.
/// </summary>
[PublicAPI]
public readonly record struct PlayerName(
    string FirstName,
    string LastName,
    ImmutableArray<string> Nicknames,
    string FullName,
    string DisplayName
) {
    /// <summary>
    /// Represents an unknown or unset player name.
    /// </summary>
    public static readonly PlayerName Unknown = new("", "", [], "", "");

    /// <summary>
    /// The player's first name (given name).
    /// </summary>
    public string FirstName { get; init; } = FirstName;

    /// <summary>
    /// The player's last name (family name).
    /// </summary>
    public string LastName { get; init; } = LastName;

    /// <summary>
    /// Known nicknames or aliases for the player.
    /// </summary>
    public ImmutableArray<string> Nicknames { get; init; } = Nicknames;

    /// <summary>
    /// The full name (first + last).
    /// </summary>
    public string FullName { get; init; } = FullName;

    /// <summary>
    /// The primary display name (first nickname if available, otherwise full name).
    /// </summary>
    public string DisplayName { get; init; } = DisplayName;

    /// <summary>
    /// Whether this is an unknown/unset name.
    /// </summary>
    public bool IsUnknown => this == Unknown;

    /// <summary>
    /// Creates a PlayerName from first and last name.
    /// </summary>
    public static PlayerName From(string firstName, string lastName, params string[] nicknames) {
        var nicknamesArray = nicknames.ToImmutableArray();
        var fullName       = ComputeFullName(firstName, lastName);
        var displayName    = nicknamesArray.Length > 0 ? nicknamesArray[0] : fullName;

        return new(firstName, lastName, nicknamesArray, fullName, displayName);
    }

    /// <summary>
    /// Creates a PlayerName from first and last name with a list of nicknames.
    /// </summary>
    public static PlayerName From(string firstName, string lastName, ImmutableArray<string> nicknames) {
        var fullName    = ComputeFullName(firstName, lastName);
        var displayName = nicknames.Length > 0 ? nicknames[0] : fullName;

        return new(firstName, lastName, nicknames, fullName, displayName);
    }

    /// <summary>
    /// Creates a PlayerName with only a single name (mononym, like "Pelé" or "Ronaldinho").
    /// </summary>
    public static PlayerName FromMononym(string name) {
        var nicknames = ImmutableArray.Create(name);

        return new("", name, nicknames, name, name);
    }

    static string ComputeFullName(string firstName, string lastName) =>
        string.IsNullOrEmpty(firstName)
            ? lastName
            : string.IsNullOrEmpty(lastName)
                ? firstName
                : $"{firstName} {lastName}";

    public override string ToString() => DisplayName;
}
