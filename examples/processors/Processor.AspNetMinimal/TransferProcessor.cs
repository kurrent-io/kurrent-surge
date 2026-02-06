using Kurrent.Processors;
using Transfermarkt.Domain;

namespace Processor.AspNetMinimal;

public class TransferProcessor : IRecordProcessor {
    public ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            switch (record.Value) {
                case PlayerTransferred transfer:
                    context.Logger.LogInformation(
                        "Transfer: {PlayerName} from {FromClub} to {ToClub} for {Fee:C}",
                        transfer.PlayerName.ToString(),
                        transfer.FromClubId.ToString(),
                        transfer.ToClubId.ToString(),
                        transfer.Fee.Amount
                    );
                    break;

                case PlayerRegistered registered:
                    context.Logger.LogInformation(
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
