---
title: HTTP sink
description: Post records to any HTTP endpoint.
---

`http-sink` · **No licence needed** · KurrentDB 24.10 and later

Sends each record to an HTTP endpoint. One of two connectors that run without a licence key, which
makes it the usual choice for trying connectors out.

## Settings

| Key | Required | Default | Meaning |
|---|---|---|---|
| `url` | yes | | Target URL. Accepts the template parameters `{schema-subject}`, `{event-type}` and `{stream}` |
| `method` | | `POST` | HTTP method |
| `defaultHeaders` | | `Accept-Encoding:*` | Add your own as `defaultHeaders:<Name>` |
| `pooledConnectionLifetime` | | `00:05:00` | Connection pool lifetime |
| `authentication:method` | | `None` | `None`, `Basic` or `Bearer` |
| `authentication:basic:username` | | | |
| `authentication:basic:password` | | | |
| `authentication:bearer:token` | | | |

This sink adds no resilience settings of its own and uses the shared block.

Everything in [settings every sink takes](/client/connectors/#settings-every-sink-takes) applies on
top of these.

Server reference:
[HTTP sink](https://docs.kurrent.io/server/v26.1/features/connectors/sinks/http).
