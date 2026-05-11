---
title: Migrate your project
index: false
---

# Migrate your project

<CopyMarkdownButton />

Move an existing project to the v2 .NET SDK. Pick the page that matches your current client.

- [From EventStoreDB](./from-eventstore-grpc.md) — the original EventStoreDB gRPC packages (`EventStore.Client.Grpc.*`).
- [From KurrentDB v1](./from-kurrentdb-v1.md) — the v1 KurrentDB .NET client.

Connection strings using the legacy `esdb://` scheme keep working. The breaking changes are in the package layout, namespaces, and APIs.
