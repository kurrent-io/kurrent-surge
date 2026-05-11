---
order: 2
---

# Connection Targets

<CopyMarkdownButton />

A connection target is the endpoint, or set of endpoints, the client connects to. The scheme and endpoints change based on the deployment, but the rest of the configuration stays the same.

## Local development

For a node started with the [Quickstart](../quickstart.md) (Docker, no TLS), the client only needs the address and a flag to disable TLS.

::: tabs#connect-local

@tab Connection string

```csharp
await using var client = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");
```

@tab Options

```csharp
var options = new KurrentClientOptions {
    Endpoints = [new("localhost", 2113)],
    Security  = KurrentClientSecurityOptions.Insecure
};

await using var client = KurrentClient.Create(options);
```

:::

## Single production node

For a single production node, drop `?tls=false` and supply credentials. The default `kurrentdb` scheme enables TLS automatically, and the client refuses plaintext when TLS is on.

::: tabs#connect-single

@tab Connection string

```csharp
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@kurrentdb.internal:2113");
```

@tab Options

```csharp
var options = new KurrentClientOptions {
    Endpoints = [new("kurrentdb.internal", 2113)],
    Security  = KurrentClientSecurityOptions.Basic("admin", "changeit")
};

await using var client = KurrentClient.Create(options);
```

:::

## Cluster

For multi-node deployments, switch the scheme to `kurrentdb+discover` and list the seed nodes. The client uses gossip to discover the full cluster, then routes operations based on the configured node preference.

::: tabs#cluster-discovery

@tab Connection string

```csharp
await using var client = KurrentClient.Create("kurrentdb+discover://admin:changeit@node1:2113,node2:2113,node3:2113?nodePreference=Follower");
```

@tab Options

```csharp
var options = new KurrentClientOptions {
    ConnectionScheme = KurrentConnectionScheme.Discover,
    Endpoints        = [
        new("node1", 2113),
        new("node2", 2113),
        new("node3", 2113)
    ],
    Security = KurrentClientSecurityOptions.Basic("admin", "changeit"),
    Gossip   = KurrentClientGossipOptions.Default with {
        ReadPreference = NodePreference.Follower
    }
};

await using var client = KurrentClient.Create(options);
```

:::

Available node preferences:

| Preference        | Description                          |
|-------------------|--------------------------------------|
| `Leader`          | Always route to the cluster leader   |
| `Follower`        | Prefer a follower for reads          |
| `ReadOnlyReplica` | Prefer a read-only replica           |
| `Random`          | Pick any reachable node (default)    |

For custom CA, mutual TLS, and other security configuration, see [Security](./security.md).
