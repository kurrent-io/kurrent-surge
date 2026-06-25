// ReSharper disable ClassNeverInstantiated.Global

using Transfermarkt.Domain.Events;

namespace Processor.AspNetMinimal;

public class LiveTransferProcessor : MessageProcessor {
    public LiveTransferProcessor() {
        Process<PlayerTransferred>((transfer, ctx) => {
            ctx.Logger.LogInformation(
                "Transfer: {PlayerName} from {FromClub} to {ToClub} for {Fee:C}",
                transfer.PlayerName, transfer.FromClubId.ToString(), transfer.ToClubId.ToString(), transfer.Fee.Amount);
        });

        Process<PlayerRegistered>((registered, ctx) => {
            ctx.Logger.LogInformation(
                "Registration: {PlayerName} ({Nationality}) {Position}",
                registered.Name.ToString(), registered.Nationality.ToString(), registered.MainPosition.ToString());
        });
    }
}
