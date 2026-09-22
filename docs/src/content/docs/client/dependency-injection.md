---
title: Wiring Up Surge
description: One entry point. AddKurrentSurge opens a scope that carries the connection and everything that runs against it.
---

`AddKurrentSurge` is the only door. It opens a registration scope that carries the connection and
every component that runs against it.

```csharp
using Kurrent.Surge;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddKurrentSurge(surge => {
    surge.Connection("kurrentdb://localhost:2113");
    surge.AddProcessor<OrderProcessor>(opts =>
        opts.Subscription.Filter = RecordFilter.ByCategory("orders"));
});

await builder.Build().RunAsync();
```

The scope registers a `KurrentClient` for injection, and the processor runs as a hosted service that
the host starts and stops.

[Running a Processor](/kurrent-surge/processors/running-a-processor/) shows how to host a processor,
read its health check, and handle its failure.

One `using` covers the lambda. The builder's methods are declared on `IKurrentSurgeBuilder` rather
than as extension methods, so a call site needs no `using` for the options namespaces.

## Entry points

| Call | On | Connection comes from |
|---|---|---|
| `services.AddKurrentSurge(configure)` | `IServiceCollection` | the `Connection` call inside, or configuration |
| `builder.AddKurrentSurge(configure)` | `IHostApplicationBuilder` | the same |
| `builder.AddKurrentSurge(connectionName, configure?)` | `IHostApplicationBuilder` | `ConnectionStrings:{connectionName}` |

The named overload is the Aspire shape: the argument is the name of the KurrentDB resource, not a
connection string.

```csharp
builder.AddKurrentSurge("kurrentdb");
```

It throws `KurrentClientNotConfiguredException` at registration when that key is absent, rather
than starting a host that fails later.

## The connection

Four overloads, all on the builder.

```csharp
surge.Connection("kurrentdb://localhost:2113");

surge.Connection("kurrentdb://localhost:2113", opts =>
    opts.Resilience = KurrentClientResilienceOptions.Subscription);

surge.Connection(KurrentClientOptions.Parse("kurrentdb://localhost:2113"));

surge.Connection(opts =>
    opts.Resilience = KurrentClientResilienceOptions.Subscription);
```

`Connection` is authoritative. It replaces any `KurrentClientOptions` or `KurrentClient` already in
the service collection, and the last call wins.

The two string overloads start from a blank options object, so a configured connection cannot leak
into one you stated explicitly. The lambda-only overload is different: with no string it seeds from
the configuration chain, so you are tuning the configured connection rather than declaring a new
one.

Passing pre-built options validates them immediately, so a bad connection surfaces at registration
instead of on first use.

## Processors

`AddProcessor<T>` has four overloads. Two take the processor id first. Each takes the
per-processor settings as `Action<ProcessorOptions>`, or as `Action<ProcessorOptions, IServiceProvider>`
when those settings need something resolved.

```csharp
IKurrentSurgeBuilder AddProcessor<T>(Action<ProcessorOptions>? configure = null);
IKurrentSurgeBuilder AddProcessor<T>(string processorId, Action<ProcessorOptions>? configure = null);
IKurrentSurgeBuilder AddProcessor<T>(Action<ProcessorOptions, IServiceProvider> configure);
IKurrentSurgeBuilder AddProcessor<T>(string processorId, Action<ProcessorOptions, IServiceProvider> configure);
```

`AddBatchProcessor<T>` has the same four overloads for `IRecordBatchProcessor`. See
[writing a processor](/kurrent-surge/processors/writing-a-processor/) for the two handler contracts.

```csharp
surge.AddProcessor<OrderProcessor>("orders-eu", (opts, sp) =>
    opts.Subscription.Filter = sp.GetRequiredService<IFilterPolicy>().Current);
```

The first argument, `orders-eu`, is the processor id.

The scope registers `T` in the container as a singleton, keyed by the processor id. It uses
`TryAdd`, so a keyed registration of your own for that id wins. The scope registers `T` and nothing
else. The dependencies of `T` are yours to register, the way you register any service.

Each processor id gets one hosted service, so the same type runs several times under different ids:
one per tenant, or one per feed. A duplicate id throws `InvalidOperationException`, and the message
names the id.

The health check comes with the registration. Each processor gets one check, named by its id.
`IDrone` is registered keyed by the id too.

Resolve a processor elsewhere by its id: `GetRequiredKeyedService<OrderProcessor>("orders-eu")`, or
`[FromKeyedServices("orders-eu")]` on a minimal API parameter. While a type runs under one id only,
`GetRequiredService<OrderProcessor>()` resolves it as well. A second id for the same type removes
that unkeyed registration. A `services.AddSingleton<OrderProcessor>()` of your own is a separate
instance that the processor runtime never uses.

`surge.FailProcessOnFatalError()` makes `RunAsync` throw the exception of a failed processor, so the
process exits with a failure code on .NET 10. See
[Failure and exit code](/kurrent-surge/processors/running-a-processor/#failure-and-exit-code).

## Configuration

A processor reads one section, `Kurrent:Surge:Processors:{id}`, before the configure delegate runs,
so code overrides the file.

```json
{
  "Kurrent": {
    "Surge": {
      "Processors": {
        "order-processor": {
          "Subscription": { "InitialPosition": "Earliest" },
          "Checkpoint": { "Interval": 5000 }
        }
      }
    }
  }
}
```

The id is the one you pass at registration. Without one, it is the type name in kebab-case, so
`OrderProcessor` reads `order-processor` with no configuration on your part. The processor
`orders-eu` above reads `Kurrent:Surge:Processors:orders-eu`. Case, `-`, and `_` do not matter in
the section name.

The scope reads no other section. Configuration cannot set the id: the scope logs a `ProcessorId`
key as an unrecognised setting. Setting `ProcessorId` in the delegate does not change the section
that the scope read.

The scope logs configuration problems as warnings, and invalid values fail host startup.

## Connection precedence

Most specific first. The first one that resolves is used.

| # | Source |
|---|---|
| 1 | `opts.Connection(...)` on the processor's own options |
| 2 | `ConnectionString` or `Connection` in `Kurrent:Surge:Processors:{ProcessorId}` |
| 3 | The scope's `Connection(...)` |
| 4 | `ConnectionStrings:Kurrent:Surge` |
| 5 | `ConnectionStrings:KurrentDB` |

Sources 2 to 5 resolve before the configure delegate runs, so the delegate receives the connection
the processor will use. A connection the delegate sets replaces it.

A processor's own section outranks the scope so one processor can be pointed at another cluster
from configuration alone.

`ConnectionStrings` keys are matched with separators stripped and case ignored, so
`OrderProcessor`, `order-processor` and `order_processor` all reach the same entry.

## What the scope does not register

**Multiple connections.** The scope registers at most one. An application that needs a write
connection and a read connection builds its own options and clients and hands them where they are
needed.

```csharp
var writes = KurrentClient.Create("kurrentdb+discover://cluster:2113?nodePreference=leader");
var reads  = KurrentClient.Create("kurrentdb+discover://cluster:2113?nodePreference=follower");

builder.Services.AddSingleton(new OrderWriter(writes));
builder.Services.AddSingleton(new OrderReader(reads));
```

Node preference is bound to the client, so two preferences means two clients. They share nothing
internally, and the cost is one extra set of channels.

**Module clients.** `StreamsClient`, `RegistryClient`, `IndexesClient`, `ConnectorsClient` and
`AdminClient` have no registration surface. Reach them through the injected client:
`client.Streams`, `client.Registry`, `client.Indexes`, `client.Connectors`, `client.Admin`.

**Catalogs and codecs.** Catalogs come from the assembly attribute or from
`opts.Messages.UseCatalog(...)`, and the codecs are built in. See
[catalogs](/kurrent-surge/schema/catalogs/).

Resolving a client when nothing configured a connection throws
`KurrentClientNotConfiguredException` on first resolve rather than connecting to a default.
