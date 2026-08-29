---
title: Partitioning
description: Spread one stream across several processor instances without losing per-key order.
---

Partitioning lets several instances of the same processor share the work, while every record for a
given key still goes to one instance in order.

:::caution[Not configurable yet]
The runtime below is real and runs. The knob that drives it is not finished: the partition key
selector is not part of the public API, and `MaxParallelism` above 1 requires it. Leave
`MaxParallelism` at its default of 1 until it lands. See [Concurrency inside one
instance](#concurrency-inside-one-instance).
:::

## The pieces

| Piece | Role |
|---|---|
| `PartitionKeyExtractor` | derives the key from a record |
| `XXHash` | hashes the key to a partition number |
| `IDistributionStrategy` | assigns partitions to instances |
| `PartitionAssigner` | applies the assignment |
| `DistributionManifest` | the shared view of who owns what |

## Strategies

**Range** splits the partition space into contiguous blocks, one per instance. Simple, and
rebalancing moves large blocks at once.

**Sticky** keeps an instance's existing partitions where it can and moves only what it must. Costlier
to compute, and far cheaper when instances come and go. That matters if your processor holds
per-key state in memory, because a moved partition means that state has to be rebuilt.

## Ordering

Order is guaranteed per partition, not globally. Records sharing a key hash to the same partition, so
they arrive in order and at one instance. Records with different keys have no ordering relationship
across partitions.

Choose the key accordingly: a key that is too coarse leaves one partition hot, and a key too fine
gives up the ordering you wanted.

## Concurrency inside one instance

`MaxParallelism` is separate from partitioning and defaults to 1, so a single instance processes one
record at a time.

```csharp
opts.MaxParallelism = 1;   // the default, and the only value that starts today
```

Raising it needs a partition key selector, and that selector is not on the public surface yet, so a
processor configured above 1 fails validation before it connects:

```
Subscription.PartitionKeySelector is required when MaxParallelism is greater than 1.
```

Once it lands, raising it will let one instance work on several records concurrently, which gives up
ordering *within* the instance unless your handler tolerates it.

Partitioning scales out across processes. `MaxParallelism` scales up inside one. They compose, and
they cost different guarantees.
