---
order: 4
---

# Security

<CopyMarkdownButton />

The `kurrentdb` scheme enables TLS by default, and the `Basic` preset always pairs username/password credentials with standard TLS. Transport (how the channel is encrypted) and authentication (how the client identifies itself) are configured separately, but the presets on `KurrentClientSecurityOptions` set sensible pairings, and validation warns if you compose basic credentials with an unencrypted transport directly.

## Authenticate with a username and password

The `Basic` preset enables standard TLS and adds username and password credentials.

::: tabs#auth-basic

@tab Connection string

```csharp
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@kurrentdb.internal:2113");
```

@tab Options

```csharp
var options = KurrentClientOptions.Parse("kurrentdb://kurrentdb.internal:2113") with {
    Security = KurrentClientSecurityOptions.Basic("admin", "changeit")
};

await using var client = KurrentClient.Create(options);
```

:::

## Authenticate with a client certificate

The `MutualTls` preset uses a client certificate and key for authentication. If the server is fronted by a private certificate authority, supply its root CA at the same time.

::: tabs#auth-mtls

@tab Connection string

```csharp
await using var client = KurrentClient.Create("kurrentdb://kurrentdb.internal:2113?userCertFile=/certs/client.crt&userKeyFile=/certs/client.key&tlsCaFile=/certs/ca.crt");
```

@tab Options

```csharp
var options = KurrentClientOptions.Parse("kurrentdb://kurrentdb.internal:2113") with {
    Security = KurrentClientSecurityOptions.MutualTls(
        "/certs/client.crt",
        "/certs/client.key",
        "/certs/ca.crt")
};

await using var client = KurrentClient.Create(options);
```

:::

For an mTLS cluster, combine certificate authentication with discovery:

```csharp
await using var client = KurrentClient.Create("kurrentdb+discover://node1:2113,node2:2113,node3:2113?userCertFile=/certs/client.crt&userKeyFile=/certs/client.key&nodePreference=Follower");
```

If you already hold the certificates as `X509Certificate2` objects, for example loaded from a key vault, pass them directly:

```csharp
var options = KurrentClientOptions.Parse("kurrentdb://kurrentdb.internal:2113") with {
    Security = KurrentClientSecurityOptions.MutualTls(clientCertificate, rootCaCertificate)
};
```

## Trust a private certificate authority

When the server presents a certificate signed by a CA that is not in the system trust store, point the client at the CA file or pass a certificate object.

```csharp
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@kurrentdb.internal:2113?tlsCaFile=/certs/ca.crt");
```

For combinations the factories do not cover, compose transport and authentication directly on the options record:

```csharp
var options = KurrentClientOptions.Parse("kurrentdb://kurrentdb.internal:2113") with {
    Security = new() {
        Transport      = TransportSecurity.Certificate("/certs/ca.crt"),
        Authentication = ClientCredentials.Basic("admin", "changeit")
    }
};
```

## Disable TLS for local development

A node started with the Quickstart Docker command listens in insecure mode. Match it with `?tls=false`, or assign `KurrentClientSecurityOptions.Insecure` on the options record.

```csharp
await using var client = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");
```

See [Connection Targets](./connection-targets.md#local-development) for the full local setup.

## Disable server certificate verification

`?tlsVerifyCert=false` keeps the channel encrypted but skips certificate validation. Only use this against test environments. A production deployment should rely on a trusted CA or supply its CA file with `tlsCaFile`.

```csharp
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@kurrentdb.internal:2113?tlsVerifyCert=false");
```

## Connection string parameters

These parameters control transport and authentication. Names are case-insensitive.

| Parameter       | Type   | Default | Description                          |
|-----------------|--------|---------|--------------------------------------|
| `tls`           | bool   | `true`  | Enable TLS transport security        |
| `tlsVerifyCert` | bool   | `true`  | Verify the server TLS certificate    |
| `tlsCaFile`     | string | _none_  | Path to a custom CA certificate file |
| `userCertFile`  | string | _none_  | Path to client certificate (mTLS)    |
| `userKeyFile`   | string | _none_  | Path to client private key (mTLS)    |

For non-security parameters, see [Connection Options](./connection-options.md#connection-string-reference).
