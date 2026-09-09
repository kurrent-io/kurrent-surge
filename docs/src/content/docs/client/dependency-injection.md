---
title: Wiring Up Surge
description: One entry point. AddKurrentSurge opens a scope that carries the connection and everything that runs against it.
---

`AddKurrentSurge` is the only door. It opens a registration scope that carries the connection and
every component that runs against it.

```csharp
using Kurrent.Surge;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<OrderProcessor>();

builder.Services.AddKurrentSurge(surge => {
    surge.Connection("kurrentdb://localhost:2113");
    surge.AddProcessor<OrderProcessor>(opts =>
        opts.Subscription.Filter = RecordFilter.ByCategory("orders"));
});

await builder.Build().RunAsync();
```

The scope registers a `KurrentClient` for injection, and the processor runs as a hosted service that
the host starts and stops.

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

`AddProcessor<T>` and `AddBatchProcessor<T>` each carry six overloads over three ways of getting the
processor.

```csharp
// Resolved from the container. Register it yourself.
builder.Services.AddSingleton<OrderProcessor>();
surge.AddProcessor<OrderProcessor>();

// Built by a factory, once per drone at host startup.
surge.AddProcessor(sp => new OrderProcessor(sp.GetRequiredService<IOrderStore>()));

// A pre-built instance, reused for the drone's lifetime.
surge.AddProcessor(new OrderProcessor());
```

The type overload resolves `T` and never registers it, so an unregistered processor fails at host
startup. The factory and instance overloads do not add the type to the container.

Each of the three takes per-processor settings, either as `Action<ProcessorOptions>` or as
`Action<ProcessorOptions, IServiceProvider>` when those settings need something resolved.

```csharp
surge.AddProcessor<OrderProcessor>((opts, sp) => {
    opts.ProcessorId = "orders-eu";
    opts.Subscription.Filter = sp.GetRequiredService<IFilterPolicy>().Current;
});
```

`AddBatchProcessor` is the same six shapes for `IRecordBatchProcessor`. See
[writing a processor](/kurrent-surge/processors/writing-a-processor/) for the two handler contracts.

## Configuration

A processor reads `Kurrent:Surge:Processors:{name}` before the configure delegate runs, so code
overrides the file.

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

The scope matches the section by the processor's type name first. `ProcessorId` defaults to the type
name in kebab-case, so `OrderProcessor` reads `order-processor` with no configuration on your part.

Set a custom `ProcessorId` and a section named after it is read too, this time **over** the
configure delegate. A section named by a deliberate per-deployment id is treated as an override, so
it can retarget one processor without a code change. The connection is the one exception: an
explicit `Connection` call in your code still wins.

The scope logs configuration problems as warnings, and invalid values fail host startup.

## Connection precedence

Most specific first. The first one that resolves is used.

| # | Source |
|---|---|
| 1 | `opts.Connection(...)` on the processor's own options |
| 2 | `ConnectionStrings:{ProcessorId}` |
| 3 | The scope's `Connection(...)` |
| 4 | `ConnectionStrings:KurrentSurge` |
| 5 | `ConnectionStrings:KurrentDB` |

A processor's own key outranks the scope so one processor can be pointed at another cluster from
configuration alone.

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
