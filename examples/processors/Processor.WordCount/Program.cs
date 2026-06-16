using Kurrent.Client;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams;
using Kurrent.Streaming;
using Kurrent.Streaming.Models;
using Processor.WordCount;
using Processor.WordCount.Events;
using Refit;
using Serilog;
using Serilog.Extensions.Logging;
using UselessFacts;

// Word Count processor - classic stream processing example
// Continuously seeds TextSubmitted events (fetched from the uselessfacts API)
// into a per-run stream and processes them. Press Ctrl+C to shutdown.

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var stream = $"WordCount-{Guid.NewGuid().ToString()[..8]}";

Log.Information("Seeding stream {Stream} with TextSubmitted events", stream);

var catalog = new MessageCatalog("wordcount").Map<TextSubmitted>();

var factsApi = RestService.For<IUselessFactsApi>("https://uselessfacts.jsph.pl");

var seeder = Task.Run(() => SeedAsync(stream, factsApi, catalog, cts.Token));

var processor = KurrentProcessor
    .Create(new WordCountProcessor(), opts => {
        opts.Connection("kurrentdb://admin:changeit@localhost:2113?tls=false", client => {
            client.LoggerFactory = new SerilogLoggerFactory();
            client.Messages.UseCatalog(catalog);
        });
        
        opts.Subscription.Filter          = RecordFilter.ByStream(stream);
        opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest; 
        
        opts.Debug = true;
    })
    .RunAsync(cts.Token)
    .AsTask();

try {
    await Task.WhenAll(seeder, processor);
}
catch (OperationCanceledException) {
    // expected on Ctrl+C
}
finally {
    await Log.CloseAndFlushAsync();
}

return;

static async Task SeedAsync(string stream, IUselessFactsApi factsApi, MessageCatalog catalog, CancellationToken ct) {
    var delay = TimeSpan.FromSeconds(1);

    await using var client = KurrentClient.Create(
        "kurrentdb://admin:changeit@localhost:2113?tls=false", client => client.Messages.UseCatalog(catalog));

    Log.Information("Streaming facts continuously. Press Ctrl+C to stop.");

    while (!ct.IsCancellationRequested) {
        var fact = await FetchFact(factsApi, ct);

        var record = AppendRecord.New()
            .Stream(stream)
            .Value(new TextSubmitted(Guid.NewGuid().ToString(), fact, DateTimeOffset.UtcNow))
            .Build();
        
        await client.Streams.AppendStream(stream, [record], ct);
        
        Log.Information("{Fact}", fact);

        await Task.Delay(delay, ct);
    }

    static async Task<string> FetchFact(IUselessFactsApi api, CancellationToken ct) {
        const int maxAttempts = 3;

        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++) {
            try {
                var fact = await api.GetRandomFact(ct);
                return fact.Text;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) {
                lastError = ex;
                Log.Warning("Fact fetch attempt {Attempt}/{Max} failed: {Error}", attempt, maxAttempts, ex.Message);
            }

            if (attempt < maxAttempts) await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
        }

        throw new InvalidOperationException($"Fact fetch failed after {maxAttempts} attempts", lastError);
    }
}