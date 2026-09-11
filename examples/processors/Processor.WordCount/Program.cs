using Kurrent.Surge;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors.Models;
using Kurrent.Surge.Schema.Catalogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Processor.WordCount;
using Processor.WordCount.Events;
using Serilog;
using Serilog.Events;
using UselessFacts;

// Word Count processor - classic stream processing example
// Continuously seeds TextSubmitted events (fetched from the uselessfacts API)
// into a per-run stream and processes them. Press Ctrl+C to shutdown.

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .WriteTo.Console()
    .CreateLogger();

try {
    var stream  = $"WordCount-{Guid.NewGuid().ToString()[..8]}";
    var catalog = new MessageCatalog("wordcount").Map<TextSubmitted>();

    var builder = SurgeProcessor.CreateBuilder<WordCountProcessor>(args, options => {
        options.Subscription.Filter          = RecordFilter.ByStream(stream);
        options.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;

        options.Debug = true;
    });

    builder.AddKurrentSurge(surge => surge.Connection(client => client.Messages.UseCatalog(catalog)));

    builder.Services.AddSerilog();
    builder.Services.AddUselessFactsClient();

    builder.Services.AddHostedService(sp => ActivatorUtilities.CreateInstance<FactSeeder>(sp, stream));

    await builder.Build().RunAsync();
}
catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally {
    await Log.CloseAndFlushAsync();
}
