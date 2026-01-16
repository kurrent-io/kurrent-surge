using Kurrent.Client.Streams;
using Processor.AspNetMinimal;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // Register shared Kurrent client
    builder.Services.AddKurrentClient("kurrentdb://localhost:2113");

    // Register processor as hosted service
    builder.Services.AddKurrentProcessor<TransferProcessor>(config => config
        .WithFilter("schema_name IN ('PlayerTransferred', 'PlayerRegistered')")
    );

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
