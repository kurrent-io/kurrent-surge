using System.Text.Json;
using Kurrent.Surge;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Errors;
using Kurrent.Surge.Schema;
using Microsoft.Extensions.DependencyInjection;
using Registry.Quickstart;
using Registry.Quickstart.Events;
using Serilog;
using JsonSerializer = System.Text.Json.JsonSerializer;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var services = new ServiceCollection()
    .AddKurrentSurge(surge => surge.Connection(
        "kurrentdb://admin:changeit@localhost:2113?tls=false",
        opts => opts.Schema = KurrentClientSchemaOptions.AutoRegister));

await using var provider = services.BuildServiceProvider();

var client = provider.GetRequiredService<KurrentClient>();

var stream    = $"sensors-{Guid.NewGuid():N}";
var ingestion = new IngestionService(client);
var dashboard = new DashboardService(client);

// Platform: register schema with backward compatibility
try {
    await client.Registry.CreateSchema(
        Schemas.TemperatureReading, Schemas.V1,
        SchemaDataFormat.Json, CompatibilityMode.Backward);

    Log.Information("[platform] Registered schema {Schema} v1", Schemas.TemperatureReading);
}
catch (KurrentException) {
    Log.Information("[platform] Schema {Schema} already registered", Schemas.TemperatureReading);
}

// Ingestion writes, dashboard reads
await ingestion.WriteReadings(stream,
    new TemperatureReading("sensor-1", 22.5, DateTimeOffset.UtcNow),
    new TemperatureReading("sensor-2", 18.3, DateTimeOffset.UtcNow),
    new TemperatureReading("sensor-1", 23.1, DateTimeOffset.UtcNow));

await dashboard.ShowReadings(stream);

// Ingestion v2: breaking rename blocked, safe evolution succeeds
await ingestion.CheckCompatibility(Schemas.BreakingRename);
await ingestion.EvolveSchema(Schemas.V2WithLocation);

await ingestion.WriteReadings(stream,
    new TemperatureReading("sensor-3", 19.7, DateTimeOffset.UtcNow, "warehouse-b"));

// Dashboard: old and new events coexist
await dashboard.ShowReadings(stream);

// Platform: inspect the registry
var schema = await client.Registry.GetSchema(Schemas.TemperatureReading);
var latest = await client.Registry.GetSchemaVersion(Schemas.TemperatureReading, schema.LatestSchemaVersion);

Log.Information("[platform] {Schema}: {Compatibility}, version {Version}",
    schema.SchemaName, schema.Details.Compatibility, latest.VersionNumber);

var pretty = JsonSerializer.Serialize(
    JsonDocument.Parse(latest.SchemaDefinition),
    new JsonSerializerOptions { WriteIndented = true });

Log.Information("[platform] Latest definition:\n{Definition}", pretty);
