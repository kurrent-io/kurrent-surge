using System.Text.Json;
using Kurrent.Surge.Streaming;
using StackExchange.Redis;
using Transfermarkt.Domain.Events;

namespace Processor.WithRedisCache;

/// <summary>
/// Caches player information in Redis for fast lookups.
/// Demonstrates the cache-on-write pattern for enrichment scenarios.
/// </summary>
public sealed class PlayerCacheProcessor(
    IConnectionMultiplexer redis
) : IRecordProcessor {
    static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    IDatabase Db => redis.GetDatabase();

    public async ValueTask ProcessRecord(ProcessRecordContext context) {
        switch (context.Record.Value) {
            case PlayerRegistered registered:
                await CachePlayerInfo(registered, context.Logger);
                break;

            case PlayerTransferred transfer:
                await UpdatePlayerClub(transfer, context.Logger);
                break;
        }
    }

    async ValueTask CachePlayerInfo(PlayerRegistered registered, ILogger logger) {
        var playerData = new CachedPlayer {
            PlayerId    = registered.PlayerId.ToString(),
            Name        = registered.Name.ToString(),
            Nationality = registered.Nationality.ToString(),
            Position    = registered.MainPosition.ToString(),
            DateOfBirth = registered.DateOfBirth.ToString("yyyy-MM-dd"),
            CurrentClub = null,
            UpdatedAt   = DateTimeOffset.UtcNow.ToString("O")
        };

        var key  = $"player:{registered.PlayerId}";
        var json = JsonSerializer.Serialize(playerData);

        await Db.StringSetAsync(key, json, CacheDuration);
        logger.LogDebug("Cached player: {PlayerId} ({Name})", registered.PlayerId, registered.Name);
    }

    async ValueTask UpdatePlayerClub(PlayerTransferred transfer, ILogger logger) {
        var key = $"player:{transfer.PlayerId}";

        // Try to get existing cached data
        var existing = await Db.StringGetAsync(key);

        CachedPlayer playerData;
        if (existing.HasValue) {
            playerData = JsonSerializer.Deserialize<CachedPlayer>(existing.ToString())!;
            playerData = playerData with {
                Name        = transfer.PlayerName.ToString(),
                CurrentClub = transfer.ToClubId.IsUnset ? null : transfer.ToClubId.ToString(),
                UpdatedAt   = DateTimeOffset.UtcNow.ToString("O")
            };
        } else {
            // Create minimal record if not in cache
            playerData = new CachedPlayer {
                PlayerId    = transfer.PlayerId.ToString(),
                Name        = transfer.PlayerName.ToString(),
                CurrentClub = transfer.ToClubId.IsUnset ? null : transfer.ToClubId.ToString(),
                UpdatedAt   = DateTimeOffset.UtcNow.ToString("O")
            };
        }

        var json = JsonSerializer.Serialize(playerData);
        await Db.StringSetAsync(key, json, CacheDuration);

        logger.LogDebug("Updated player: {PlayerId} -> {Club}", transfer.PlayerId, playerData.CurrentClub ?? "retired");
    }

    public ValueTask OnCheckpoint(CheckpointContext context) {
        context.Logger.LogInformation("Checkpoint {CheckpointId}", context.CheckpointId);
        return ValueTask.CompletedTask;
    }

    record CachedPlayer {
        public string  PlayerId    { get; init; } = "";
        public string  Name        { get; init; } = "";
        public string? Nationality { get; init; }
        public string? Position    { get; init; }
        public string? DateOfBirth { get; init; }
        public string? CurrentClub { get; init; }
        public string  UpdatedAt   { get; init; } = "";
    }
}
