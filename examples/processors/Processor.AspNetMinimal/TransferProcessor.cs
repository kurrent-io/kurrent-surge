using Kurrent.Client.Streams;
using Transfermarkt.Domain;

namespace Processor.AspNetMinimal;

/// <summary>
/// Processor that logs transfers using ILogger.
/// Demonstrates proper logging integration with Serilog.
/// </summary>
public sealed class TransferProcessor(ILogger<TransferProcessor> logger) : IRecordProcessor {
    public ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            switch (record.Value) {
                case PlayerTransferred transfer:
                    logger.LogInformation(
                        "Transfer: {PlayerName} from {FromClub} to {ToClub} for {Fee:C}",
                        transfer.PlayerName.ToString(),
                        transfer.FromClubId.ToString(),
                        transfer.ToClubId.ToString(),
                        transfer.Fee.Amount
                    );
                    break;

                case PlayerRegistered registered:
                    logger.LogInformation(
                        "Registration: {PlayerName} ({Nationality}, {Position})",
                        registered.Name.ToString(),
                        registered.Nationality.ToString(),
                        registered.MainPosition.ToString()
                    );
                    break;
            }
        }

        return ValueTask.CompletedTask;
    }
}
