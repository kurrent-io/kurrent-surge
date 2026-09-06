using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Processor.Leaderboard.Events;

namespace Processor.Leaderboard;

public sealed class ScoreSeeder(KurrentClient client, ILogger<ScoreSeeder> logger) : BackgroundService {
    const string GameId = "main";

    static readonly (string PlayerId, string DisplayName, string Country)[] Players = [
        ("player123", "Ada", "UK"),
        ("player456", "Grace", "US"),
        ("player789", "Linus", "FI"),
        ("player321", "Yukihiro", "JP"),
        ("player654", "Guido", "NL")
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // The processor subscribes from the latest position; give it a moment to attach.
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        foreach (var (playerId, displayName, country) in Players) {
            var stream = $"leaderboard-player-{playerId}";
            var joined = new PlayerJoined(playerId, displayName, country, DateTimeOffset.UtcNow);

            await client.Streams.AppendStream(stream, [AppendRecord.Create(joined, r => r.Stream = stream)], stoppingToken);
        }

        logger.LogInformation("[seed] {Count} players joined game {GameId}", Players.Length, GameId);

        while (!stoppingToken.IsCancellationRequested) {
            var player = Players[Random.Shared.Next(Players.Length)];
            var score  = new ScoreSubmitted(GameId, player.PlayerId, Random.Shared.Next(100, 10_000), DateTimeOffset.UtcNow);
            var stream = $"leaderboard-game-{GameId}";

            await client.Streams.AppendStream(stream, [AppendRecord.Create(score, r => r.Stream = stream)], stoppingToken);

            logger.LogInformation("[seed] {PlayerId} scored {Score}", score.PlayerId, score.Score);

            await Task.Delay(TimeSpan.FromMilliseconds(750), stoppingToken);
        }
    }
}
