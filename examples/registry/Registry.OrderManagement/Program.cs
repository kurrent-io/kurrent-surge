using Kurrent.Client;
using Kurrent.Client.Registry;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization.Json;
using Registry.OrderManagement;
using Registry.OrderManagement.Events;
using Registry.OrderManagement.Schemas;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddSingleton(_ => {
        var opts = KurrentClientOptions.Parse("kurrentdb://admin:changeit@localhost:2113?tls=false");
        opts.Schema = new KurrentClientSchemaOptions {
            Json = new JsonSchemaSerializerOptions {
                SchemaRegistration = SchemaRegistrationOptions.AutoRegister
            }
        };
        return opts;
    });
    builder.Services.AddKurrentClient();

    builder.Services.AddSingleton<SchemaService>();
    builder.Services.AddSingleton<OrderService>();

    var app = builder.Build();

    app.MapGet("/health", () => Results.Ok("Healthy"));

    app.MapPost("/schemas/seed", async (SchemaService svc) => {
        var runId = Guid.NewGuid().ToString()[..8];

        var placed = await svc.Register(
            $"orders.order-placed-{runId}", SeedSchemas.OrderPlacedV1,
            "Customer placed an order", CompatibilityMode.Backward,
            SeedSchemas.Domain, SeedSchemas.Team);

        var shipped = await svc.Register(
            $"orders.order-shipped-{runId}", SeedSchemas.OrderShippedV1,
            "Order shipped to customer", CompatibilityMode.Backward,
            SeedSchemas.Domain, SeedSchemas.Team);

        return Results.Ok(new {
            RunId = runId,
            Schemas = new[] {
                new { Schema = $"orders.order-placed-{runId}", Version = placed.VersionNumber, Id = placed.VersionId.Value },
                new { Schema = $"orders.order-shipped-{runId}", Version = shipped.VersionNumber, Id = shipped.VersionId.Value }
            }
        });
    });

    app.MapPost("/schemas", async (RegisterSchemaRequest req, SchemaService svc) => {
        var compatibility = Enum.Parse<CompatibilityMode>(req.Compatibility, ignoreCase: true);
        var result = await svc.Register(
            req.SchemaName, req.Definition, req.Description,
            compatibility, req.Domain, req.Team);

        return Results.Created($"/schemas/{req.SchemaName}", new {
            SchemaName    = req.SchemaName,
            VersionNumber = result.VersionNumber,
            VersionId     = result.VersionId.Value
        });
    });

    app.MapPost("/schemas/{name}/versions", async (string name, EvolveSchemaRequest req, SchemaService svc) => {
        var result = await svc.Evolve(name, req.Definition);
        return Results.Ok(new {
            SchemaName    = name,
            VersionNumber = result.VersionNumber,
            VersionId     = result.VersionId.Value
        });
    });

    app.MapPost("/schemas/{name}/check", async (string name, CheckCompatibilityRequest req, SchemaService svc) => {
        var result = await svc.CheckCompatibility(name, req.Definition);
        return Results.Ok(new {
            SchemaName   = name,
            IsCompatible = result.IsCompatible,
            Errors       = result.Errors.Select(e => new { e.Kind, e.PropertyPath, e.Details })
        });
    });

    app.MapGet("/schemas/{name}", async (string name, SchemaService svc) => {
        var schema = await svc.GetInfo(name);
        return Results.Ok(new {
            SchemaName    = schema.SchemaName.Value,
            LatestVersion = schema.LatestSchemaVersion,
            Compatibility = schema.Details.Compatibility.ToString(),
            Description   = schema.Details.Description,
            Tags          = schema.Details.Tags,
            CreatedAt     = schema.CreatedAt,
            UpdatedAt     = schema.UpdatedAt
        });
    });

    app.MapGet("/schemas/{name}/versions/{versionNumber:int}", async (string name, int versionNumber, SchemaService svc) => {
        var version = await svc.GetVersion(name, versionNumber);
        return Results.Ok(new {
            VersionNumber    = version.VersionNumber,
            VersionId        = version.VersionId.Value,
            SchemaDefinition = version.SchemaDefinition,
            RegisteredAt     = version.RegisteredAt
        });
    });

    app.MapGet("/schemas/resolve/{versionId}", async (string versionId, SchemaService svc) => {
        var version = await svc.ResolveVersion(SchemaVersionId.From(versionId));
        return Results.Ok(new {
            VersionId        = version.VersionId.Value,
            VersionNumber    = version.VersionNumber,
            SchemaDefinition = version.SchemaDefinition,
            RegisteredAt     = version.RegisteredAt
        });
    });

    app.MapGet("/schemas", async (SchemaService svc, string? prefix, string? team) => {
        var schemas = await svc.Discover(prefix, team);
        return Results.Ok(schemas.Select(s => new {
            SchemaName    = s.SchemaName.Value,
            LatestVersion = s.LatestSchemaVersion,
            Compatibility = s.Details.Compatibility.ToString(),
            Description   = s.Details.Description,
            Tags          = s.Details.Tags
        }));
    });

    app.MapDelete("/schemas/{name}", async (string name, SchemaService svc) => {
        await svc.Delete(name);
        return Results.Ok(new { SchemaName = name, Status = "deleted" });
    });

    app.MapPost("/orders", async (PlaceOrderRequest req, OrderService svc) => {
        var order = new OrderPlaced(
            Guid.NewGuid().ToString(),
            req.CustomerId,
            req.Items.Select(i => new OrderItem(i.Sku, i.Quantity, i.UnitPrice)).ToList(),
            req.Items.Sum(i => i.Quantity * i.UnitPrice),
            DateTimeOffset.UtcNow,
            req.ShippingAddress,
            req.DiscountCode);

        var revision = await svc.PlaceOrder(order);
        return Results.Created($"/orders/{order.OrderId}/events", new {
            OrderId  = order.OrderId,
            Total    = order.Total,
            Revision = revision.Value
        });
    });

    app.MapPost("/orders/{orderId}/ship", async (string orderId, ShipOrderRequest req, OrderService svc) => {
        var shipped = new OrderShipped(orderId, req.Carrier, req.TrackingNumber, DateTimeOffset.UtcNow);
        var revision = await svc.ShipOrder(shipped);
        return Results.Ok(new { OrderId = orderId, Revision = revision.Value });
    });

    app.MapGet("/orders/{orderId}/events", async (string orderId, OrderService svc) => {
        var events = await svc.GetEvents(orderId);
        return Results.Ok(events);
    });

    await app.RunAsync();
}
catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally {
    await Log.CloseAndFlushAsync();
}

record RegisterSchemaRequest(string SchemaName, string Definition, string Description, string Compatibility, string Domain, string Team);
record EvolveSchemaRequest(string Definition);
record CheckCompatibilityRequest(string Definition);
record PlaceOrderRequest(string CustomerId, List<OrderItemRequest> Items, string? ShippingAddress = null, string? DiscountCode = null);
record OrderItemRequest(string Sku, int Quantity, decimal UnitPrice);
record ShipOrderRequest(string Carrier, string TrackingNumber);
