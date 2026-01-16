using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;
using Transfermarkt.Domain;

namespace Processor.Minimal;

/// <summary>
/// The simplest possible processor - just implements ProcessRecords.
/// No lifecycle hooks, no state, no dependencies.
/// </summary>
public sealed class MinimalProcessor(ILogger<MinimalProcessor> logger) : IRecordProcessor {
    public ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            // Records are decoded by default - just access the Value
            switch (record.Value) {
                case PlayerTransferred transfer:
                    logger.LogInformation(
                        "[Transfer] {PlayerName} from {FromClub} to {ToClub} for {Fee}",
                        transfer.PlayerName,
                        transfer.FromClubId,
                        transfer.ToClubId,
                        transfer.Fee
                    );
                    break;

                case PlayerRegistered registered:
                    logger.LogInformation(
                        "[Registered] {PlayerName} ({Nationality}, {Position})",
                        registered.Name,
                        registered.Nationality,
                        registered.MainPosition
                    );
                    break;

                case PlayerRetired retired:
                    logger.LogInformation("[Retired] Player {PlayerId}", retired.PlayerId);
                    break;

                default:
                    logger.LogInformation("[Unknown] {SchemaName}", record.Schema.SchemaName);
                    break;
            }
        }

        return ValueTask.CompletedTask;
    }
}
