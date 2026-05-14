---
title: Getting started
order: 2
index: false
---

# Getting started

<CopyMarkdownButton />

Hands-on guides for working with the registry. Every operation lives on `client.Registry` and returns a value directly, throwing on failure. See [Error handling](../../error-handling.md) for the shared exception model.

- [Creating a schema](./creating-a-schema.md) — register a new schema with a compatibility mode and optional metadata.
- [Getting a schema](./getting-a-schema.md) — fetch metadata, fetch a specific version, list schemas with filters.
- [Updating a schema](./updating-a-schema/README.md) — check a proposed change and register a new version.
- [Deleting a schema](./deleting-a-schema.md) — remove a schema and every version it holds.

Each page leads with the request-type API. Convenience overloads sit in a tip block under the main example.
