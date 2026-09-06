using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Processor.FraudDetection.Events;

namespace Processor.FraudDetection;

sealed class TransactionSeeder(KurrentClient client, ILogger<TransactionSeeder> logger) : BackgroundService {
    static readonly string[] Accounts  = ["acct-101", "acct-202", "acct-303", "acct-404"];
    static readonly string[] Locations = ["New York, NY", "Los Angeles, CA", "Chicago, IL", "Miami, FL"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // The processor subscribes from the latest position; give it a moment to attach.
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        var i = 0;

        while (!stoppingToken.IsCancellationRequested) {
            var account = Accounts[i % Accounts.Length];
            var stream  = $"transactions-{account}";

            if (i % 4 == 3) {
                // Card-testing pattern: a sub-dollar probe, then a large purchase from another city.
                await Append(stream, new Transaction($"tx-{i:D4}-probe", account, 0.50m, Locations[i % Locations.Length], DateTimeOffset.UtcNow), stoppingToken);
                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                await Append(stream, new Transaction($"tx-{i:D4}-large", account, Random.Shared.Next(500, 2500), Locations[(i + 1) % Locations.Length], DateTimeOffset.UtcNow), stoppingToken);
            }
            else {
                await Append(stream, new Transaction($"tx-{i:D4}", account, Random.Shared.Next(5, 400) + 0.99m, Locations[i % Locations.Length], DateTimeOffset.UtcNow), stoppingToken);
            }

            i++;

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    async Task Append(string stream, Transaction tx, CancellationToken ct) {
        await client.Streams.AppendStream(stream, [AppendRecord.Create(tx, r => r.Stream = stream)], ct);

        logger.LogInformation("[seed] {Account} ${Amount} at {Location}", tx.AccountId, tx.Amount, tx.Location);
    }
}
