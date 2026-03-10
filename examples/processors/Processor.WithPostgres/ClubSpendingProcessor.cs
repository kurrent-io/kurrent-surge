using System.Collections.Concurrent;
using Dapper;
using Kurrent.Processors;
using Npgsql;
using Transfermarkt.Domain;

namespace Processor.WithPostgres;

/// <summary>
/// Aggregates club spending and persists to Postgres on checkpoint.
/// Demonstrates stateful processing with database persistence.
/// </summary>
public sealed class ClubSpendingProcessor(NpgsqlDataSource dataSource) : IRecordProcessor {
    readonly ConcurrentDictionary<string, ClubSpending> _pending = new();

    public ValueTask ProcessRecord(ProcessRecordContext context) {
        if (context.Record.Value is not PlayerTransferred transfer)
            return ValueTask.CompletedTask;

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

        return ValueTask.CompletedTask;
    }

    public async ValueTask OnCheckpoint(CheckpointContext context) {
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
            context.Logger.LogInformation(
                "Checkpoint {CheckpointId}: flushed {RecordCount} club aggregates to Postgres",
                context.CheckpointId,
                toFlush.Length
            );
        }
        catch {
            await tx.RollbackAsync(context.CancellationToken);
            throw;
        }
    }

    public ValueTask OnComplete(CompleteContext context) {
        context.Logger.LogInformation("Completed, {PendingCount} pending records", _pending.Count);
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
