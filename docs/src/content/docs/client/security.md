---
title: Security
description: Connect over TLS, trust a private CA, and authenticate with a password or a certificate.
---

TLS is on by default and the server certificate is verified. Everything below is either turning that
off for local development, or telling the client who to trust and who you are.

## Development mode and production mode

Every example in these docs connects to a local development node, which is why they carry
`tls=false`. That is a mode you opted into, not a default. Know which one you are in.

| | Development | Production |
|---|---|---|
| Connection string | `kurrentdb://localhost:2113?tls=false` | `kurrentdb+discover://cluster:2113` |
| Transport | Plain HTTP/2, nothing encrypted | TLS, certificate verified |
| Server identity | Not checked | Checked against a CA you trust |
| Credentials | The seeded `admin` account | An identity you created, read from configuration |

:::danger
`tls=false` turns transport security off. Every record you write, every record you read and the
password in the connection string cross the network in the clear. The client will also talk to
whatever answers on that port. It exists so a local container works without certificates. Never
deploy it.
:::

Production needs no extra parameter. Drop `tls=false` and TLS is on, verified against the system
trust store.

```
kurrentdb+discover://admin:changeit@cluster.example.internal:2113
```

Read the credentials from configuration rather than a literal, the same as any other secret.

```csharp
await using var client = KurrentClient.Create(builder.Configuration.GetConnectionString("Kurrent")!);
```

## Trust a private CA

When the cluster certificate is signed by your own CA, give the client the CA bundle.

```
kurrentdb+discover://admin:changeit@cluster:2113?tlsCaFile=/certs/ca.crt
```

```csharp
opts.Security = KurrentClientSecurityOptions.MutualTls(
    clientCertPath: "/certs/client.crt",
    clientKeyPath:  "/certs/client.key",
    rootCaPath:     "/certs/ca.crt");
```

Both forms keep verification on. They change who the client trusts, not whether it checks.

## Authenticate

Three ways, and they resolve in this order.

| Method | How |
|---|---|
| Certificate | `userCertFile` and `userKeyFile`, plus `userKeyPassword` if the key is encrypted |
| Username and password | The userinfo part of the connection string, percent-encoded |
| None | Neither supplied |

Certificate credentials win when both are present.

```
kurrentdb://admin:changeit@localhost:2113
kurrentdb://user%40domain:p%40ssw%3Ard@localhost:2113
kurrentdb://cluster:2113?userCertFile=/certs/user.crt&userKeyFile=/certs/user.key
```

In code, through a profile:

```csharp
opts.Security = KurrentClientSecurityOptions.Password("admin", "changeit");

opts.Security = KurrentClientSecurityOptions.MutualTls(clientCertificate, rootCaCertificate);
```

The second overload takes loaded `X509Certificate2` instances, for a certificate that comes from a
store or a secrets manager rather than a file.

## Run against an insecure local node

A local node started in insecure mode speaks plain HTTP/2. Say so, or the handshake fails.

```
kurrentdb://localhost:2113?tls=false
```

```csharp
opts.Security = KurrentClientSecurityOptions.Insecure;
```

## What the parameters resolve to

The three TLS parameters combine into one transport mode.

| `tls` | `tlsCaFile` | `tlsVerifyCert` | Result |
|---|---|---|---|
| `false` | any | any | No transport security |
| `true` | set | any | Verify against that CA bundle |
| `true` | unset | `true` | Verify against the system trust store |
| `true` | unset | `false` | Encrypted, **not verified** |

:::caution
`tlsVerifyCert=false` encrypts the connection and then accepts any certificate presented, which
removes the guarantee that you are talking to your cluster. It is a development convenience. Use
`tlsCaFile` in production instead, which keeps verification on against a CA you control.
:::

## Credentials are per client

Credentials belong to the client, and no operation takes its own. An application that must act as
more than one identity builds a client per identity.
