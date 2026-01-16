using System.Collections.Concurrent;
using Dapper;
using Kurrent.Client.Streams;
using Npgsql;
using Transfermarkt.Domain;

namespace Processor.WithPostgres;

/// <summary>
/// Aggregates club spending and persists to Postgres on checkpoint.
/// Demonstrates stateful processing with database persistence.
/// </summary>
public sealed class ClubSpendingProcessor(
    NpgsqlDataSource dataSource,
    ILogger<ClubSpendingProcessor> logger
) : IRecordProcessor {
    readonly ConcurrentDictionary<string, ClubSpending> _pending = new();

    public async ValueTask OnInitialize(InitializationContext context) {
        logger.LogInformation("Initializing ClubSpendingProcessor: {ProcessorId}", context.Settings.ProcessorId);

        // Ensure table exists
        await using var conn = await dataSource.OpenConnectionAsync(context.CancellationToken);
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS club_spending (
                club_id TEXT NOT NULL,
                season INT NOT NULL,
                total_spent DECIMAL(18,2) NOT NULL DEFAULT 0,
                total_received DECIMAL(18,2) NOT NULL DEFAULT 0,
                players_in INT NOT NULL DEFAULT 0,
                players_out INT NOT NULL DEFAULT 0,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY (club_id, season)
            )
            """);
    }

    public ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            if (record.Value is not PlayerTransferred transfer)
                continue;

            var season = GetSeasonForDate(transfer.EffectiveDate);

            // Update selling club (receives money)
            if (!transfer.FromClubId.IsUnset) {
                var key = $"{transfer.FromClubId}:{season}";
                _pending.AddOrUpdate(
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

            // Update buying club (spends money)
            if (!transfer.ToClubId.IsUnset) {
                var key = $"{transfer.ToClubId}:{season}";
                _pending.AddOrUpdate(
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

        return ValueTask.CompletedTask;
    }

    public async ValueTask OnCheckpointComplete(CheckpointCompleteContext context) {
        if (_pending.IsEmpty)
            return;

        // Snapshot and clear pending changes
        var toFlush = _pending.ToArray();
        _pending.Clear();

        await using var conn = await dataSource.OpenConnectionAsync(context.CancellationToken);
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
            logger.LogInformation(
                "Checkpoint #{CheckpointNumber}: flushed {RecordCount} club aggregates to Postgres",
                context.Progress.Checkpoint.TotalCheckpoints,
                toFlush.Length
            );
        }
        catch {
            await tx.RollbackAsync(context.CancellationToken);
            throw;
        }
    }

    public ValueTask OnShutdown(ShutdownContext context) {
        logger.LogInformation("Shutting down ({Reason}), {PendingCount} pending records", context.Reason, _pending.Count);
        return ValueTask.CompletedTask;
    }

    static int GetSeasonForDate(DateOnly date) {
        // Season runs July-June, so Jan-June belongs to previous year's season
        return date.Month >= 7 ? date.Year : date.Year - 1;
    }

    record ClubSpending {
        public string  ClubId        { get; init; } = "";
        public int     Season        { get; init; }
        public decimal TotalSpent    { get; init; }
        public decimal TotalReceived { get; init; }
        public int     PlayersIn     { get; init; }
        public int     PlayersOut    { get; init; }
    }
}
