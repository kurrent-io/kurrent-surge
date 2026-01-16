using System.Text.Json;
using Kurrent.Client.Streams;
using StackExchange.Redis;
using Transfermarkt.Domain;

namespace Processor.WithRedisCache;

/// <summary>
/// Caches player information in Redis for fast lookups.
/// Demonstrates the cache-on-write pattern for enrichment scenarios.
/// </summary>
public sealed class PlayerCacheProcessor(
    IConnectionMultiplexer redis,
    ILogger<PlayerCacheProcessor> logger
) : IRecordProcessor {
    static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    IDatabase Db => redis.GetDatabase();

    public ValueTask OnInitialize(InitializationContext context) {
        logger.LogInformation("Initializing PlayerCacheProcessor: {ProcessorId}", context.Settings.ProcessorId);
        return ValueTask.CompletedTask;
    }

    public async ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            switch (record.Value) {
                case PlayerRegistered registered:
                    await CachePlayerInfo(registered);
                    break;

                case PlayerTransferred transfer:
                    await UpdatePlayerClub(transfer);
                    break;
            }
        }
    }

    async ValueTask CachePlayerInfo(PlayerRegistered registered) {
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

    async ValueTask UpdatePlayerClub(PlayerTransferred transfer) {
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

    public ValueTask OnCheckpointComplete(CheckpointCompleteContext context) {
        logger.LogInformation("Checkpoint #{CheckpointNumber}", context.Progress.Checkpoint.TotalCheckpoints);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnShutdown(ShutdownContext context) {
        logger.LogInformation("Shutting down ({Reason})", context.Reason);
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
