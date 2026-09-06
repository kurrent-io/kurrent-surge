using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Processor.WordCount.Events;
using UselessFacts;

namespace Processor.WordCount;

sealed class FactSeeder(string stream, KurrentClient client, IUselessFactsApi factsApi, ILogger<FactSeeder> logger) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Streaming facts into {Stream}. Press Ctrl+C to stop.", stream);

        while (!stoppingToken.IsCancellationRequested) {
            var fact   = await FetchFact(stoppingToken);
            var record = AppendRecord.Create(new TextSubmitted(Guid.NewGuid().ToString(), fact, DateTimeOffset.UtcNow), r => r.Stream = stream);

            await client.Streams.AppendStream(stream, [record], stoppingToken);

            logger.LogInformation("{Fact}", fact);

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    async Task<string> FetchFact(CancellationToken ct) {
        const int maxAttempts = 3;

        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++) {
            try {
                var fact = await factsApi.GetRandomFact(ct);
                return fact.Text;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) {
                lastError = ex;
                logger.LogWarning("Fact fetch attempt {Attempt}/{Max} failed: {Error}", attempt, maxAttempts, ex.Message);
            }

            if (attempt < maxAttempts) await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
        }

        throw new InvalidOperationException($"Fact fetch failed after {maxAttempts} attempts", lastError);
    }
}
