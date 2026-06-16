using Kurrent.Client;
using Kurrent.Client.Streams;
using Connector.Management.Events;

namespace Connector.Management;

public class PaymentPlanService(KurrentClient client, ILogger<PaymentPlanService> logger) : BackgroundService {
    static readonly string[] Currencies = ["USD", "EUR", "GBP"];
    static readonly string[] Frequencies = ["monthly", "quarterly", "annual"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        logger.LogInformation("Payment plan service started. Writing events to paymentPlan-* streams");

        var planCounter = 0;

        while (!stoppingToken.IsCancellationRequested) {
            var planId   = $"pp-{++planCounter:D4}";
            var stream   = $"paymentPlan-{planId}";
            var currency = Currencies[Random.Shared.Next(Currencies.Length)];

            await client.Streams.AppendStream(stream, [
                AppendRecord.Create(new PaymentPlanCreated(
                    planId,
                    CustomerId: $"cust-{Random.Shared.Next(1, 100):D3}",
                    Amount: Math.Round((Random.Shared.NextDouble() * 500) + 10, 2),
                    currency,
                    Frequency: Frequencies[Random.Shared.Next(Frequencies.Length)],
                    CreatedAt: DateTimeOffset.UtcNow
                ))
            ], stoppingToken);

            logger.LogInformation("Wrote PaymentPlanCreated to {Stream} ({Currency} {Amount})",
                stream, currency, planCounter);

            if (Random.Shared.NextDouble() > 0.3) {
                var req = AppendRecord.Create(
                    new PaymentCollected(
                        planId,
                        InstallmentNumber: 1,
                        Amount: Math.Round(Random.Shared.NextDouble() * 100 + 5, 2),
                        currency,
                        CollectedAt: DateTimeOffset.UtcNow),
                    r => r.Stream = stream);

                await client.Streams.AppendStream(req, stoppingToken);

                logger.LogInformation("Wrote PaymentCollected to {Stream}", stream);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}
