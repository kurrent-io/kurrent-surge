---
title: Updating a schema
order: 3
index: false
---

# Updating a schema

<CopyMarkdownButton />

Two operations cover the update path:

- [Checking compatibility](./checking-compatibility.md) — dry-run a proposed change against an existing schema. The registry is not modified.
- [Adding a schema version](./adding-a-version.md) — register a new version. The server runs the same check and rejects incompatible changes.

The safe workflow is to check, then register. Both calls share the same exception model: `SchemaIncompatibleException` carries the list of violations, `SchemaNotFoundException` and `SchemaDeletedException` cover lifecycle errors.

The behaviour of the compatibility check is governed by the schema's compatibility mode, set on creation. See [Compatibility modes](../../compatibility-modes.md) for the rules.
