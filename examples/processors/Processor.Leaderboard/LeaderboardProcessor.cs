using System.Text.Json;
using Kurrent.Client.Streams;
using Kurrent.Processors;
using Processor.Leaderboard.Events;
using StackExchange.Redis;

namespace Processor.Leaderboard;

/// <summary>
/// Real-time gaming leaderboard processor using Redis sorted sets.
/// Maintains per-game leaderboards with player rankings.
/// Demonstrates external state management with Redis.
/// </summary>
public sealed class LeaderboardProcessor(IConnectionMultiplexer redis) : IRecordProcessor {
    IDatabase Db => redis.GetDatabase();

    long _scoresProcessed;
    long _playersRegistered;

    public async ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            switch (record.Value) {
                case PlayerJoined player:
                    await RegisterPlayer(player, context.Logger);
                    break;

                case ScoreSubmitted score:
                    await ProcessScore(score, context.Logger);
                    break;
            }
        }
    }

    async ValueTask RegisterPlayer(PlayerJoined player, ILogger logger) {
        _playersRegistered++;

        var playerData = new PlayerInfo(
            player.PlayerId,
            player.DisplayName,
            player.Country,
            player.JoinedAt.ToString("O")
        );

        // Store player info as a hash
        var key = $"player:{player.PlayerId}";
        await Db.StringSetAsync(key, JsonSerializer.Serialize(playerData));

        logger.LogInformation(
            "🎮 PLAYER JOINED | {PlayerId}: {DisplayName} ({Country})",
            player.PlayerId,
            player.DisplayName,
            player.Country
        );
    }

    async ValueTask ProcessScore(ScoreSubmitted score, ILogger logger) {
        _scoresProcessed++;

        // Get leaderboard key for this game
        var leaderboardKey = $"leaderboard:{score.GameId}";

        // Get player's current best score (Redis sorted set stores highest score automatically with ZADD GT)
        var currentBest = await Db.SortedSetScoreAsync(leaderboardKey, score.PlayerId);

        // Only update if this is a new high score for the player
        if (!currentBest.HasValue || score.Score > currentBest.Value) {
            // Update the sorted set (score is the sort value, player is the member)
            await Db.SortedSetAddAsync(leaderboardKey, score.PlayerId, score.Score);

            // Get new rank (0-based, so add 1 for human-readable rank)
            var rank = await Db.SortedSetRankAsync(leaderboardKey, score.PlayerId, Order.Descending);

            // Get player display name
            var playerKey = $"player:{score.PlayerId}";
            var playerData = await Db.StringGetAsync(playerKey);
            var displayName = score.PlayerId;
            if (playerData.HasValue) {
                var player = JsonSerializer.Deserialize<PlayerInfo>(playerData.ToString());
                displayName = player?.DisplayName ?? score.PlayerId;
            }

            logger.LogInformation(
                "🏆 NEW HIGH SCORE | Game: {GameId} | Player: {Player} | Score: {Score:N0} | Rank: #{Rank}",
                score.GameId,
                displayName,
                score.Score,
                (rank ?? 0) + 1
            );

            // Log if player made it to top 10
            if (rank is <= 9) {
                logger.LogInformation(
                    "   └─ 🌟 {Player} is now in the TOP 10!",
                    displayName
                );
            }
        } else {
            logger.LogDebug(
                "Score {Score} for {Player} in {Game} - not a personal best (current: {Best})",
                score.Score,
                score.PlayerId,
                score.GameId,
                currentBest
            );
        }
    }

    public async ValueTask OnCheckpoint(CheckpointContext context) {
        // Get stats about the main leaderboard
        var mainLeaderboard = "leaderboard:main";
        var totalPlayers = await Db.SortedSetLengthAsync(mainLeaderboard);

        context.Logger.LogInformation(
            "Checkpoint {CheckpointId} | Scores: {Scores} | Players: {Players} | Leaderboard size: {Size}",
            context.CheckpointId,
            _scoresProcessed,
            _playersRegistered,
            totalPlayers
        );
    }

    public ValueTask OnComplete(CompleteContext context) {
        context.Logger.LogInformation(
            "Completed | Total scores: {Scores}, Players registered: {Players}",
            _scoresProcessed,
            _playersRegistered
        );
        return ValueTask.CompletedTask;
    }

    record PlayerInfo(string PlayerId, string DisplayName, string Country, string JoinedAt);
}
