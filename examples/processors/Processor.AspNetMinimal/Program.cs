using Kurrent.Processors.Options;
using Processor.AspNetMinimal;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddKurrentProcessor<TransferProcessor>(cfg => cfg
        .UseClient("kurrentdb://localhost:2113")
        .WithFilter("schema IN ('PlayerTransferred', 'PlayerRegistered')"));

    var app = builder.Build();

    app.MapGet("/health", () => Results.Ok("Healthy"));

    await app.RunAsync();
}
catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally {
    await Log.CloseAndFlushAsync();
}
