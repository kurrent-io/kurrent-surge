// ReSharper disable RedundantAnonymousTypePropertyName

using Kurrent.Client;
using Kurrent.Client.Streams;
using Serilog;
using Streams.InventoryManagement;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddKurrentClient();
    builder.Services.AddSingleton<InventoryService>();

    var app = builder.Build();

    app.MapGet("/health", () => Results.Ok("Healthy"));

    app.MapPost("/stock/{sku}/receive", async (string sku, ReceiveRequest req, InventoryService svc) => {
        var revision = await svc.ReceiveStock(sku, req.Quantity, req.Warehouse, req.PurchaseOrderId);
        return Results.Ok(new { Sku = sku, Revision = revision.Value });
    });

    app.MapPost("/stock/{sku}/ship", async (string sku, ShipRequest req, InventoryService svc) => {
        var revision = await svc.ShipStock(sku, req.Quantity, req.OrderId);
        return Results.Ok(new { Sku = sku, Revision = revision.Value });
    });

    app.MapPost("/stock/{sku}/adjust", async (string sku, AdjustRequest req, InventoryService svc) => {
        var revision = await svc.AdjustStock(sku, req.Quantity, req.Reason);
        return Results.Ok(new { Sku = sku, Revision = revision.Value });
    });

    app.MapPost("/stock/{sku}/transfer", async (string sku, TransferRequest req, InventoryService svc) => {
        await svc.TransferStock(sku, req.Quantity, req.FromWarehouse, req.ToWarehouse);
        return Results.Ok(new { Sku = sku, Status = "transferred" });
    });

    app.MapGet("/stock/{sku}", async (string sku, InventoryService svc) => {
        var (level, revision) = await svc.GetStockLevel(sku);
        return Results.Ok(new { Sku = sku, Level = level, Revision = revision.Value });
    });

    app.MapGet("/stock/{sku}/history", async (string sku, InventoryService svc) =>
        Results.Ok((await svc.GetHistory(sku)).Select(ToEvent)));

    app.MapGet("/stock/{sku}/history/first", async (string sku, InventoryService svc) =>
        Results.Ok(ToEvent(await svc.GetFirstEvent(sku))));

    app.MapGet("/stock/{sku}/history/latest", async (string sku, InventoryService svc) =>
        Results.Ok(ToEvent(await svc.GetLatestEvent(sku))));

    app.MapGet("/stock/{sku}/history/since/{revision:long}", async (string sku, long revision, InventoryService svc) =>
        Results.Ok((await svc.GetEventsSince(sku, Revision.From(revision))).Select(ToEvent)));

    app.MapGet("/stock/{sku}/history/recent/{count:int}", async (string sku, int count, InventoryService svc) =>
        Results.Ok((await svc.GetRecentEvents(sku, count)).Select(ToEvent)));

    app.MapGet("/activity", async (InventoryService svc, int? max) =>
        Results.Ok((await svc.GetRecentActivity(max ?? 20)).Select(r => new {
            Stream     = r.Stream.ToString(),
            Revision   = r.Revision.Value,
            SchemaName = r.Schema.SchemaName.ToString(),
            Timestamp  = r.Timestamp
        })));

    await app.RunAsync();
}
catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally {
    await Log.CloseAndFlushAsync();
}

return;

static object ToEvent(Record r) => new {
    Revision   = r.Revision.Value,
    SchemaName = r.Schema.SchemaName.ToString(),
    Timestamp  = r.Timestamp
};

record ReceiveRequest(int Quantity, string Warehouse, string PurchaseOrderId);
record ShipRequest(int Quantity, string OrderId);
record AdjustRequest(int Quantity, string Reason);
record TransferRequest(int Quantity, string FromWarehouse, string ToWarehouse);
