using Kurrent.Surge;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Transfermarkt.Domain.Events;
using Transfermarkt.Seeding;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);

builder.AddKurrentSurge(surge => surge.AddProcessor<LiveTransferProcessor>("live-transfers", options => {
    options.Subscription.Filter          = RecordFilter.ByExpression("stream like 'player-%'");
    options.Subscription.InitialPosition = SubscriptionInitialPosition.Latest;

    // Tailing live data only, so there is nothing to resume after a restart — checkpoint
    // rarely rather than never, so the drone never wastes writes catching up to itself.
    options.Checkpoint.Interval = TimeSpan.FromDays(1);
}));

builder.Services.AddSerilog();
builder.Services.AddHostedService<TransferSeeder>();

await builder.Build().RunAsync();

sealed class LiveTransferProcessor : MessageProcessor {
    public LiveTransferProcessor() {
        Process<PlayerTransferred>((transfer, ctx) =>
            ctx.Logger.LogInformation(
                "Transfer: {PlayerName} from {FromClub} to {ToClub} for {Fee:C}",
                transfer.PlayerName, transfer.FromClubId.ToString(), transfer.ToClubId.ToString(), transfer.Fee.Amount));

        Process<PlayerRegistered>((registered, ctx) =>
            ctx.Logger.LogInformation(
                "Registration: {PlayerName} ({Nationality}) {Position}",
                registered.Name.ToString(), registered.Nationality.ToString(), registered.MainPosition.ToString()));
    }
}
