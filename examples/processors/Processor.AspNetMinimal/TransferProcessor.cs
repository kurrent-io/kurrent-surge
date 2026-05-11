// ReSharper disable ClassNeverInstantiated.Global

namespace Processor.AspNetMinimal;

public class TransferProcessor : IRecordProcessor {
    public ValueTask ProcessRecord(ProcessRecordContext context) {
        switch (context.Record.Value) {
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

        return ValueTask.CompletedTask;
    }
}
