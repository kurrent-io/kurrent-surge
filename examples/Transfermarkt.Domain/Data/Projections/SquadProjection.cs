using System.Collections.Immutable;

namespace Transfermarkt.Domain;

/// <summary>
/// Current squad for a club at a point in time.
/// Built by processing <see cref="PlayerTransferred"/> events.
/// </summary>
[PublicAPI]
public sealed record SquadProjection {
    SquadProjection(
        ClubId clubId,
        DateOnly asOfDate,
        ImmutableArray<SquadMember> members
    ) {
        ClubId   = clubId;
        AsOfDate = asOfDate;
        Members  = members;
    }

    /// <summary>
    /// The club this squad belongs to.
    /// </summary>
    public ClubId ClubId { get; }

    /// <summary>
    /// The date this squad snapshot represents.
    /// </summary>
    public DateOnly AsOfDate { get; }

    /// <summary>
    /// The players in the squad.
    /// </summary>
    public ImmutableArray<SquadMember> Members { get; }

    /// <summary>
    /// Creates a SquadProjection.
    /// </summary>
    public static SquadProjection Create(
        ClubId clubId,
        DateOnly asOfDate,
        ImmutableArray<SquadMember>? members = null
    ) => new(clubId, asOfDate, members ?? []);
}
