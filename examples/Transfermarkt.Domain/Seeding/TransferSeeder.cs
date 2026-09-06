using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Transfermarkt.Domain.Events;
using Transfermarkt.Domain.Model;

namespace Transfermarkt.Seeding;

/// <summary>
/// Registers a player and transfers them once a second, so the processor examples have live input.
/// </summary>
public sealed class TransferSeeder(KurrentClient client, ILogger<TransferSeeder> logger) : BackgroundService {
    static readonly (string FirstName, string LastName, Country Country, Position Position)[] Players = [
        ("Kylian",   "Mbappé",     Country.France,  Position.Striker),
        ("Jude",     "Bellingham", Country.England, Position.AttackingMidfield),
        ("Vinícius", "Júnior",     Country.Brazil,  Position.LeftWinger),
        ("Erling",   "Haaland",    Country.Norway,  Position.Striker),
        ("Florian",  "Wirtz",      Country.Germany, Position.AttackingMidfield)
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // The processors subscribe from the latest position; give them a moment to attach.
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        logger.LogInformation("Seeding player registrations and transfers into player-* streams");

        var clubs = Enumerable.Range(0, 5).Select(_ => ClubId.From(Guid.NewGuid())).ToArray();

        var i = 0;

        while (!stoppingToken.IsCancellationRequested) {
            try {
                var p          = Players[i % Players.Length];
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

                var result = await client.Streams.AppendStream(stream, records, ExpectedStreamState.NoStream, stoppingToken);

                logger.LogDebug("Seeded {Player} → stream {Stream} at revision {Revision}",
                    $"{p.FirstName} {p.LastName}", stream, result.Revision);

                i++;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                logger.LogWarning(ex, "Seed error (will retry)");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
