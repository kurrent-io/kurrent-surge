---
title: Type system
order: 3
index: false
---

# Type system

<CopyMarkdownButton />

Two pieces decide how .NET types and schema names line up:

- [Type mapping](./type-mapping.md) — the bidirectional `MessageTypeMapper` between schema names and .NET types. Used on both encode (which name to write) and decode (which type to instantiate).
- [Naming strategies](./naming-strategies.md) — the rule that turns a .NET type into a schema name when no explicit mapping exists.

The strategy generates the name. The mapper stores it. Together they let you append a value of any registered type and have reads return the same .NET type without explicit dispatch.
