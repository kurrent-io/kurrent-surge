---
title: Indexes
description: Create and control secondary indexes.
---

A secondary index gives the server a routable way to find records that a log scan would otherwise
have to walk.

```csharp
ValueTask Create(...)
ValueTask Get(...)
ValueTask List(...)
ValueTask Start(...)
ValueTask Stop(...)
ValueTask Delete(...)
```

## Lifecycle

An index is created, then started. Starting begins population, and a fresh index ingests from the
beginning of the log — so on a large log it is not queryable the instant it starts. Stopping halts
population without deleting it.

## Reading through one

Once populated, filter through it:

```csharp
RecordFilter.ByUserIndex("orders-by-customer")
```

That routes the read to the index rather than scanning.

:::caution
Indexes accumulate. Creating many and never deleting them degrades the server over time — each one
is a projection the node maintains. Delete indexes you no longer read.
:::
