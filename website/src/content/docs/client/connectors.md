---
title: Connectors
description: Create, configure and run server-side connectors.
---

A connector is a server-side sink: KurrentDB pushes records out to somewhere else without your
process being in the path. Ten operations manage their whole lifecycle.

```csharp
ValueTask Create(ConnectorId, IConfiguration settings, CancellationToken)
ValueTask Reconfigure(ConnectorId, IConfiguration settings, CancellationToken)
ValueTask Rename(...)
ValueTask Reset(...)
ValueTask Start(...)
ValueTask Stop(...)
ValueTask Delete(...)
ValueTask<ConnectorSettings>      GetSettings(...)
ValueTask<ConnectorConfiguration> GetConfiguration(...)
ValueTask<IReadOnlyList<Connector>> List(ConnectorListOptions, CancellationToken)
```

## The usual shape

Create-or-reconfigure on startup, then start. Creating a connector that exists throws, so the
idempotent form catches it:

```csharp
try {
    await client.Connectors.Create(connectorId, settings);
}
catch (ConnectorAlreadyExistsException) {
    await client.Connectors.Reconfigure(connectorId, settings);
}

await client.Connectors.Start(connectorId);
```

Settings come from `IConfiguration`, so a connector is configured the same way as the rest of your
application:

```csharp
var settings = app.Configuration.GetSection("Connector:Settings");
```

## Reset

`Reset` moves a connector's position — the way to make it reprocess from a chosen point without
recreating it.

## Connector or processor

A connector runs on the server and needs no process of yours. A [processor](/processors/) runs in
your application, where your own code and dependencies are. Use a connector to move records to a
known destination; use a processor when the handling is your logic.
