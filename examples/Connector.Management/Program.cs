using Connector.Management;
using Kurrent.Client;
using Kurrent.Client.Connectors;
using Kurrent.Client.Exceptions;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try {
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddKurrentClient();
    builder.Services.AddHostedService<PaymentPlanService>();

    var app = builder.Build();

    var client = app.Services.GetRequiredService<KurrentClient>();
    var config = app.Configuration.GetSection("Connector");

    var connectorId = ConnectorId.From(config["Id"]!);
    var settings    = config.GetSection("Settings");

    try {
        await client.Connectors.Create(connectorId, settings);
        Log.Information("Created connector {ConnectorId}", connectorId);
    }
    catch (ConnectorAlreadyExistsException) {
        await client.Connectors.Reconfigure(connectorId, settings);
        Log.Information("Connector {ConnectorId} already exists, reconfigured with latest settings", connectorId);
    }

    await client.Connectors.Start(connectorId);
    Log.Information("Connector {ConnectorId} started", connectorId);

    app.MapGet("/health", async (KurrentClient kurrent) => {
        var result = await kurrent.Connectors.List(new ConnectorListOptions {
            ConnectorIds = [connectorId]
        });

        var connector = result.Items.FirstOrDefault();

        if (connector is null)
            return Results.Problem("Connector not found", statusCode: 503);

        return connector.IsRunning
            ? Results.Ok(new {
                connector.ConnectorId,
                connector.State,
                connector.Position
            })
            : Results.Problem($"Connector is {connector.State}", statusCode: 503);
    });

    app.MapGet("/status", async (KurrentClient kurrent) => {
        var result = await kurrent.Connectors.List(new ConnectorListOptions {
            ConnectorIds    = [connectorId],
            IncludeSettings = true
        });

        var connector = result.Items.FirstOrDefault();

        return connector is null
            ? Results.NotFound()
            : Results.Ok(new {
                connector.ConnectorId,
                connector.Name,
                connector.InstanceTypeName,
                connector.State,
                connector.Position,
                connector.Settings,
                connector.CreateTime,
                connector.StateUpdateTime
            });
    });

    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

    lifetime.ApplicationStopping.Register(() => {
        Log.Information("Stopping connector {ConnectorId}...", connectorId);

        client.Connectors.Stop(connectorId)
            .AsTask().GetAwaiter().GetResult();

        Log.Information("Connector {ConnectorId} stopped", connectorId);
    });

    await app.RunAsync();
}
catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally {
    await Log.CloseAndFlushAsync();
}
