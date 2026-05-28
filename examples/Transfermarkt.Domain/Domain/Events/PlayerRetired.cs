using Transfermarkt.Domain.Model;

namespace Transfermarkt.Domain.Events;

/// <summary>
/// A player retires from professional football.
/// </summary>
/// <remarks>
/// SAGA PATTERN: Emitted by a saga that watches for <see cref="PlayerTransferred"/>
/// events where <c>ToClubId</c> is <see cref="ClubId.None"/> (player leaving the system).
/// </remarks>
[PublicAPI]
public sealed record PlayerRetired {
    /// <summary>
    /// The retiring player's identifier.
    /// </summary>
    public PlayerId PlayerId { get; init; }

    /// <summary>
    /// The player's name.
    /// </summary>
    public PlayerName PlayerName { get; init; }

    /// <summary>
    /// The player's last club before retirement.
    /// </summary>
    public ClubId LastClubId { get; init; }

    /// <summary>
    /// The date of retirement.
    /// </summary>
    public DateOnly RetirementDate { get; init; }

    /// <summary>
    /// The transfer that triggered this retirement event.
    /// </summary>
    public TransferId TriggeringTransferId { get; init; }

    /// <summary>
    /// When the retirement was recorded.
    /// </summary>
    public DateTimeOffset RetiredAt { get; init; }

}
