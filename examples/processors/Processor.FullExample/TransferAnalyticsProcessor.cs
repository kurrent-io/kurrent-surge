using System.Collections.Concurrent;
using System.Text.Json;
using Dapper;
using Kurrent.Processors;
using Npgsql;
using StackExchange.Redis;
using Transfermarkt.Domain;

namespace Processor.FullExample;

/// <summary>
/// Production-ready processor combining Postgres persistence with Redis caching.
/// Tracks club spending (Postgres) and caches player lookups (Redis).
/// </summary>
public sealed class TransferAnalyticsProcessor(
    NpgsqlDataSource postgres,
    IConnectionMultiplexer redis
) : IRecordProcessor {
    static readonly TimeSpan PlayerCacheTtl = TimeSpan.FromHours(24);

    readonly ConcurrentDictionary<string, ClubSpending> _pendingSpending = new();

    IDatabase Redis => redis.GetDatabase();

    public async ValueTask ProcessRecord(ProcessRecordContext context) {
        switch (context.Record.Value) {
            case PlayerRegistered registered:
                await CachePlayer(registered, context.Logger);
                break;

            case PlayerTransferred transfer:
                await UpdatePlayerCache(transfer);
                AccumulateSpending(transfer);
                break;
        }
    }

    async ValueTask CachePlayer(PlayerRegistered registered, ILogger logger) {
        var data = new CachedPlayer {
            PlayerId    = registered.PlayerId.ToString(),
            Name        = registered.Name.ToString(),
            Nationality = registered.Nationality.ToString(),
            Position    = registered.MainPosition.ToString(),
            DateOfBirth = registered.DateOfBirth.ToString("yyyy-MM-dd")
        };

        var key = $"player:{registered.PlayerId}";
        await Redis.StringSetAsync(key, JsonSerializer.Serialize(data), PlayerCacheTtl);
        logger.LogDebug("Cached player: {PlayerId}", registered.PlayerId);
    }

    async ValueTask UpdatePlayerCache(PlayerTransferred transfer) {
        var key = $"player:{transfer.PlayerId}";
        var existing = await Redis.StringGetAsync(key);

        var data = existing.HasValue
            ? JsonSerializer.Deserialize<CachedPlayer>(existing.ToString())! with {
                Name        = transfer.PlayerName.ToString(),
                CurrentClub = transfer.ToClubId.IsUnset ? null : transfer.ToClubId.ToString()
            }
            : new CachedPlayer {
                PlayerId    = transfer.PlayerId.ToString(),
                Name        = transfer.PlayerName.ToString(),
                CurrentClub = transfer.ToClubId.IsUnset ? null : transfer.ToClubId.ToString()
            };

        await Redis.StringSetAsync(key, JsonSerializer.Serialize(data), PlayerCacheTtl);
    }

    void AccumulateSpending(PlayerTransferred transfer) {
        var season = GetSeasonForDate(transfer.EffectiveDate);

        // Selling club receives money
        if (!transfer.FromClubId.IsUnset) {
            var key = $"{transfer.FromClubId}:{season}";
            _pendingSpending.AddOrUpdate(
                key,
                _ => new ClubSpending {
                    ClubId        = transfer.FromClubId.ToString(),
                    Season        = season,
                    TotalReceived = transfer.Fee.Amount,
                    PlayersOut    = 1
                },
                (_, existing) => existing with {
                    TotalReceived = existing.TotalReceived + transfer.Fee.Amount,
                    PlayersOut    = existing.PlayersOut + 1
                }
            );
        }

        // Buying club spends money
        if (!transfer.ToClubId.IsUnset) {
            var key = $"{transfer.ToClubId}:{season}";
            _pendingSpending.AddOrUpdate(
                key,
                _ => new ClubSpending {
                    ClubId     = transfer.ToClubId.ToString(),
                    Season     = season,
                    TotalSpent = transfer.Fee.Amount,
                    PlayersIn  = 1
                },
                (_, existing) => existing with {
                    TotalSpent = existing.TotalSpent + transfer.Fee.Amount,
                    PlayersIn  = existing.PlayersIn + 1
                }
            );
        }
    }

    public async ValueTask OnCheckpoint(CheckpointContext context) {
        if (_pendingSpending.IsEmpty) {
            context.Logger.LogDebug("Checkpoint {CheckpointId}: no pending changes", context.CheckpointId);
            return;
        }

        var toFlush = _pendingSpending.ToArray();
        _pendingSpending.Clear();

        await using var conn = await postgres.OpenConnectionAsync(context.CancellationToken);
        await using var tx = await conn.BeginTransactionAsync(context.CancellationToken);

        try {
            foreach (var (_, spending) in toFlush) {
                await conn.ExecuteAsync("""
                    INSERT INTO club_spending (club_id, season, total_spent, total_received, players_in, players_out, updated_at)
                    VALUES (@ClubId, @Season, @TotalSpent, @TotalReceived, @PlayersIn, @PlayersOut, NOW())
                    ON CONFLICT (club_id, season) DO UPDATE SET
                        total_spent = club_spending.total_spent + EXCLUDED.total_spent,
                        total_received = club_spending.total_received + EXCLUDED.total_received,
                        players_in = club_spending.players_in + EXCLUDED.players_in,
                        players_out = club_spending.players_out + EXCLUDED.players_out,
                        updated_at = NOW()
                    """,
                    spending,
                    tx
                );
            }

            await tx.CommitAsync(context.CancellationToken);
            context.Logger.LogInformation("Checkpoint {CheckpointId}: flushed {RecordCount} club aggregates",
                context.CheckpointId, toFlush.Length);
        }
        catch {
            await tx.RollbackAsync(context.CancellationToken);
            throw;
        }
    }

    static int GetSeasonForDate(DateOnly date) =>
        date.Month >= 7 ? date.Year : date.Year - 1;

    record ClubSpending {
        public string  ClubId        { get; init; } = "";
        public int     Season        { get; init; }
        public decimal TotalSpent    { get; init; }
        public decimal TotalReceived { get; init; }
        public int     PlayersIn     { get; init; }
        public int     PlayersOut    { get; init; }
    }

    record CachedPlayer {
        public string  PlayerId    { get; init; } = "";
        public string  Name        { get; init; } = "";
        public string? Nationality { get; init; }
        public string? Position    { get; init; }
        public string? DateOfBirth { get; init; }
        public string? CurrentClub { get; init; }
    }
}
