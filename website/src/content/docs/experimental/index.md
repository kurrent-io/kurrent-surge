---
title: Experimental
description: What experimental means here, and what is in this section today.
---

Experimental describes the surface, not the quality. What lives here is built, measured and tested.
What is not settled is the shape of the API, and this section is the only place that says so.

## What it means

**It ships in the package.** There is no separate install, no preview flag, and no compiler warning.
`Kurrent.Surge` carries these components and you can call them today.

**The shape can still move.** Names, signatures, and defaults can change between releases. Pin the
version you build against, and check what changed before you move to a new one.

**Nothing here is half-built.** The Appender is tested against a live server, and its throughput is
measured.

## What is in it

| Component | What it is for |
|---|---|
| [Appender](/kurrent-surge/experimental/appender/) | The fastest way to write records: buffered, batched, and shaped like a producer |
