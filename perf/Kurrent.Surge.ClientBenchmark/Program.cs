using System.CommandLine;
using System.Globalization;
using ClientBenchmark;
using Kurrent.Surge.Experimental;

// The report is parsed by scripts, so numbers use a fixed decimal separator.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var appenderDefaults = new AppenderOptions();

var connOpt = new Option<string>("--conn") {
    Description         = "KurrentDB connection string",
    DefaultValueFactory = _ => "kurrentdb://admin:changeit@127.0.0.1:2113?tls=false"
};

var streamOpt = new Option<string>("--stream") {
    Description         = "Stream name to append to",
    DefaultValueFactory = _ => "benchmark-stream"
};

var eventsOpt = new Option<int>("--events") {
    Description         = "Number of events to append",
    DefaultValueFactory = _ => 1000
};

var sizeOpt = new Option<int>("--size") {
    Description         = "Size of each event payload in bytes",
    DefaultValueFactory = _ => 1024
};

var noResilienceOpt = new Option<bool>("--no-resilience") {
    Description = "Disable the client's retry pipeline"
};

var maxBatchCountOpt = new Option<int>("--max-batch-count") {
    Description         = "Appender: records per batch",
    DefaultValueFactory = _ => appenderDefaults.MaxBatchCount
};

var lingerOpt = new Option<double>("--linger-ms") {
    Description         = "Appender: milliseconds a partial batch waits before it is flushed",
    DefaultValueFactory = _ => appenderDefaults.LingerTimeout.TotalMilliseconds
};

eventsOpt.Validators.Add(result => {
    if (result.GetValueOrDefault<int>() < 1)
        result.AddError("--events must be at least 1.");
});

sizeOpt.Validators.Add(result => {
    if (result.GetValueOrDefault<int>() < 0)
        result.AddError("--size must not be negative.");
});

var appendRecordsCmd = new Command("append-records", "Sequential appends, one record per AppendRecords call. Same shape as the upstream Rust client benchmark.") {
    connOpt, streamOpt, eventsOpt, sizeOpt, noResilienceOpt
};

var appenderCmd = new Command("appender", "Pipelined appends through the experimental Appender. Latency is submit-to-acknowledgement.") {
    connOpt, streamOpt, eventsOpt, sizeOpt, noResilienceOpt, maxBatchCountOpt, lingerOpt
};

var rootCmd = new RootCommand("KurrentDB append benchmark for the Kurrent.Surge client") { appendRecordsCmd, appenderCmd };

appendRecordsCmd.SetAction(async (result, ct) => {
    var options = ReadOptions(result);

    BenchmarkReport.Header("append-records", options);

    var run = await AppendRecordsBenchmark.Run(options, ct);

    BenchmarkReport.Results(run);
    return 0;
});

appenderCmd.SetAction(async (result, ct) => {
    var options  = ReadOptions(result);
    var settings = new AppenderBenchmark.Settings(
        result.GetValue(maxBatchCountOpt),
        TimeSpan.FromMilliseconds(result.GetValue(lingerOpt)));

    BenchmarkReport.Header("appender", options);
    Console.WriteLine($"Appender: max batch count {settings.MaxBatchCount}, linger {settings.LingerTimeout.TotalMilliseconds} ms");

    var run = await AppenderBenchmark.Run(options, settings, ct);

    BenchmarkReport.Results(run);
    return 0;
});

return await rootCmd.Parse(args).InvokeAsync();

BenchmarkOptions ReadOptions(ParseResult result) => new(
    result.GetValue(connOpt)!,
    result.GetValue(streamOpt)!,
    result.GetValue(eventsOpt),
    result.GetValue(sizeOpt),
    result.GetValue(noResilienceOpt));
