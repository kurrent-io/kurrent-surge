---
title: Migration
description: Move an existing project onto Kurrent Surge from an earlier Kurrent or EventStoreDB client.
---

Kurrent Surge replaces the earlier .NET clients for KurrentDB. Pick the guide that matches what you
have today.

- [From EventStoreDB](/kurrent-surge/migration/from-eventstoredb/), for the `EventStore.Client.Grpc.*` packages.
- [From KurrentDB v1](/kurrent-surge/migration/from-kurrentdb-v1/), for the `KurrentDB.Client.*` packages.

Both migrations are the same shape, because the v1 KurrentDB client was a rebrand of the
EventStoreDB one. The differences are in the package names you remove and the namespaces you replace.

## Your connection strings keep working

This is the one thing you do not have to change. Kurrent Surge accepts every scheme the older
clients used:

| Scheme | Meaning |
|---|---|
| `kurrentdb://` | Single node |
| `kurrentdb+discover://` | Cluster, with gossip discovery |
| `esdb://` | Legacy alias for `kurrentdb://` |
| `esdb+discover://` | Legacy alias for `kurrentdb+discover://` |
| `eventstore://` | Legacy alias for `kurrentdb://` |
| `eventstore+discover://` | Legacy alias for `kurrentdb+discover://` |

Connection strings held in environment variables, config files, or a secrets manager do not need to
change before you migrate. Every parameter behaves identically across all six schemes, including
`tls`, `tlsVerifyCert`, `keepAliveInterval`, and `nodePreference`.

Durations in a connection string are read in the same units every KurrentDB SDK uses. See
[Breaking changes](/kurrent-surge/migration/breaking-changes/) for the behaviour that moved.

## What actually breaks

The package layout, the namespaces, and the API shape. In return you get one package instead of
four, one client instead of one per feature, and typed exceptions instead of a single catch-all.
