using Kurrent.Client;
using Transfermarkt.Domain.Events;
using Transfermarkt.Domain.Model;

namespace Processor.AspNetMinimal;

public static class SeedData {
    public static void StartSeeding() {
        _ = Task.Run(async () => {
            await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113?tls=false");

            var players = new[] {
                (FirstName: "Kylian",   LastName: "Mbappé",    Country: Country.France,  Position: Position.Striker),
                (FirstName: "Jude",     LastName: "Bellingham", Country: Country.England, Position: Position.AttackingMidfield),
                (FirstName: "Vinícius", LastName: "Júnior",     Country: Country.Brazil,  Position: Position.LeftWinger),
                (FirstName: "Erling",   LastName: "Haaland",    Country: Country.Norway,  Position: Position.Striker),
                (FirstName: "Florian",  LastName: "Wirtz",      Country: Country.Germany, Position: Position.AttackingMidfield),
            };

            var clubs = new[] {
                ClubId.From(Guid.NewGuid()),
                ClubId.From(Guid.NewGuid()),
                ClubId.From(Guid.NewGuid()),
                ClubId.From(Guid.NewGuid()),
                ClubId.From(Guid.NewGuid()),
            };

            var i = 0;

            while (true) {
                try {
                    var p          = players[i % players.Length];
                    var playerId   = PlayerId.New();
                    var transferId = TransferId.New();
                    var stream     = $"player-{playerId.Value.ToString()[..8]}";

                    var registered = new PlayerRegistered {
                        PlayerId             = playerId,
                        Name                 = PlayerName.From(p.FirstName, p.LastName),
                        DateOfBirth          = new DateOnly(1998 + (i % 5), 1 + (i % 12), 1 + (i % 28)),
                        Nationality          = p.Country,
                        MainPosition         = p.Position,
                        OtherPositions       = [],
                        InitialClubId        = clubs[i % clubs.Length],
                        TriggeringTransferId = transferId,
                        RegisteredAt         = DateTimeOffset.UtcNow
                    };

                    var transferred = new PlayerTransferred {
                        TransferId    = transferId,
                        PlayerId      = playerId,
                        PlayerName    = PlayerName.From(p.FirstName, p.LastName),
                        FromClubId    = clubs[i % clubs.Length],
                        ToClubId      = clubs[(i + 1) % clubs.Length],
                        Type          = TransferType.Permanent,
                        EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
                        Fee           = Money.InEuros(Random.Shared.Next(5, 200) * 1_000_000m),
                        LoanTerms     = LoanTerms.None,
                        TransferredAt = DateTimeOffset.UtcNow
                    };

                    var records = new[] {
                        AppendRecord.Create(registered, r => r.Stream = stream),
                        AppendRecord.Create(transferred, r => r.Stream = stream)
                    };

                    var result = await client.Streams.AppendStream(stream, records, ExpectedStreamState.NoStream);

                    Log.Verbose("Seeded {Player} → stream {Stream} at revision {Revision}",
                        $"{p.FirstName} {p.LastName}", stream, result.Revision);

                    i++;
                }
                catch (Exception ex) {
                    Log.Warning(ex, "Seed error (will retry)");
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        });
    }
}
