using System.Collections.Immutable;
using Bogus;
using Transfermarkt.Domain;

namespace Kurrent.Client.Tests.Schema.Serialization.Fixtures;

public static class TransfermarktFakers {
	public static readonly Faker<PlayerTransferred> PlayerTransferred = new Faker<PlayerTransferred>()
		.RuleFor(x => x.TransferId, _ => TransferId.New())
		.RuleFor(x => x.PlayerId, _ => PlayerId.New())
		.RuleFor(x => x.PlayerName, f => PlayerName.From(f.Person.FirstName, f.Person.LastName))
		.RuleFor(x => x.FromClubId, _ => ClubId.New())
		.RuleFor(x => x.ToClubId, _ => ClubId.New())
		.RuleFor(x => x.Type, f => f.PickRandom<TransferType>())
		.RuleFor(x => x.EffectiveDate, f => DateOnly.FromDateTime(f.Date.Past()))
		.RuleFor(x => x.Fee, f => Money.InEuros(f.Finance.Amount(0, 100_000_000)))
		.RuleFor(x => x.LoanTerms, _ => LoanTerms.None)
		.RuleFor(x => x.TransferredAt, f => f.Date.RecentOffset());

	public static readonly Faker<PlayerRetired> PlayerRetired = new Faker<PlayerRetired>()
		.RuleFor(x => x.PlayerId, _ => PlayerId.New())
		.RuleFor(x => x.PlayerName, f => PlayerName.From(f.Person.FirstName, f.Person.LastName))
		.RuleFor(x => x.LastClubId, _ => ClubId.New())
		.RuleFor(x => x.RetirementDate, f => DateOnly.FromDateTime(f.Date.Past()))
		.RuleFor(x => x.TriggeringTransferId, _ => TransferId.New())
		.RuleFor(x => x.RetiredAt, f => f.Date.RecentOffset());

	public static readonly Faker<PlayerRegistered> PlayerRegistered = new Faker<PlayerRegistered>()
		.RuleFor(x => x.PlayerId, _ => PlayerId.New())
		.RuleFor(x => x.Name, f => PlayerName.From(f.Person.FirstName, f.Person.LastName))
		.RuleFor(x => x.DateOfBirth, f => DateOnly.FromDateTime(f.Date.Past(30)))
		.RuleFor(x => x.Nationality, f => f.PickRandom(Country.All.AsEnumerable()))
		.RuleFor(x => x.MainPosition, f => f.PickRandom(Position.All.AsEnumerable()))
		.RuleFor(x => x.OtherPositions, f => ImmutableArray.Create(f.PickRandom(Position.All.AsEnumerable())))
		.RuleFor(x => x.InitialClubId, _ => ClubId.New())
		.RuleFor(x => x.TriggeringTransferId, _ => TransferId.New())
		.RuleFor(x => x.RegisteredAt, f => f.Date.RecentOffset());
}
