using Transfermarkt.Domain.Model;

namespace Transfermarkt.Domain.Commands;

/// <summary>
/// Command to retire a player from professional football.
/// </summary>
[PublicAPI]
public sealed record RetirePlayer {
    /// <summary>
    /// The player being retired.
    /// </summary>
    public PlayerId PlayerId { get; init; }

    /// <summary>
    /// The date of retirement.
    /// </summary>
    public DateOnly RetirementDate { get; init; }
}
