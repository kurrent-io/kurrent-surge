using System.Text.Json;
using Kurrent.Surge.Streaming;
using Processor.Leaderboard.Events;
using StackExchange.Redis;

namespace Processor.Leaderboard;

/// <summary>
/// Real-time gaming leaderboard processor using Redis sorted sets.
/// Maintains per-game leaderboards with player rankings.
/// Demonstrates external state management with Redis.
/// </summary>
public sealed class LeaderboardProcessor : MessageProcessor {
    long _scoresProcessed;
    long _playersRegistered;

    public LeaderboardProcessor(IConnectionMultiplexer redis) {
        var db = redis.GetDatabase();
        
        Process<PlayerJoined>(async (player, ctx) => {
            _playersRegistered++;

            var playerData = new PlayerInfo(
                player.PlayerId,
                player.DisplayName,
                player.Country,
                player.JoinedAt.ToString("O"));

            // Store player info as a hash
            var key   = $"player:{player.PlayerId}";
            var value = JsonSerializer.Serialize(playerData);
            await db.StringSetAsync(key, value);

            ctx.Logger.LogInformation(
                "🎮 PLAYER JOINED | {PlayerId}: {DisplayName} ({Country})",
                player.PlayerId, player.DisplayName, player.Country);
        });
        
        Process<ScoreSubmitted>(async (score, ctx) => {
            _scoresProcessed++;

            // Get leaderboard key for this game
            var leaderboardKey = $"leaderboard:{score.GameId}";

            // Get player's current best score (Redis sorted set stores highest score automatically with ZADD GT)
            var currentBest = await db.SortedSetScoreAsync(leaderboardKey, score.PlayerId);

            // Only update if this is a new high score for the player
            if (!currentBest.HasValue || score.Score > currentBest.Value) {
                // Update the sorted set (score is the sort value, player is the member)
                await db.SortedSetAddAsync(leaderboardKey, score.PlayerId, score.Score);

                // Get new rank (0-based, so add 1 for human-readable rank)
                var rank = await db.SortedSetRankAsync(leaderboardKey, score.PlayerId, Order.Descending);

                // Get player display name
                var playerKey   = $"player:{score.PlayerId}";
                var playerData  = await db.StringGetAsync(playerKey);
                var displayName = score.PlayerId;

                if (playerData.HasValue) {
                    var player = JsonSerializer.Deserialize<PlayerInfo>(playerData.ToString());
                    displayName = player?.DisplayName ?? score.PlayerId;
                }

                ctx.Logger.LogInformation(
                    "🏆 NEW HIGH SCORE | Game: {GameId} | Player: {Player} | Score: {Score:N0} | Rank: #{Rank}",
                    score.GameId, displayName, score.Score,
                    (rank ?? 0) + 1);

                // Log if player made it to top 10
                if (rank is <= 9)
                    ctx.Logger.LogInformation("   └─ 🌟 {Player} is now in the TOP 10!", displayName);
            } else
                ctx.Logger.LogDebug(
                    "Score {Score} for {Player} in {Game} - not a personal best (current: {Best})",
                    score.Score, score.PlayerId, score.GameId,
                    currentBest);
        });
        
        OnCheckpoint(ctx => {
            var totalPlayers = db.SortedSetLength("leaderboard:main");

            ctx.Logger.LogInformation(
                "Checkpoint {CheckpointId} | Scores: {Scores} | Players: {Players} | Leaderboard size: {Size}",
                ctx.CheckpointId, _scoresProcessed, _playersRegistered, totalPlayers);

            return ValueTask.CompletedTask;
        });
    }
    
    record PlayerInfo(string PlayerId, string DisplayName, string Country, string JoinedAt);
}