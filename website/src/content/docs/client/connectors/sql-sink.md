---
title: SQL sink
description: Write records into SQL Server or PostgreSQL.
---

`sql-sink` · **Licence required** · KurrentDB 26.0 and later

Writes records into SQL Server or PostgreSQL. It processes only records whose content type is
`application/json`; anything else is skipped.

## Settings

| Key | Required | Default | Meaning |
|---|---|---|---|
| `connectionString` | yes | | ADO.NET connection string |
| `type` | | `SqlServer` | `SqlServer` or `PostgreSql` |
| `reducer:mappings` | | | Base64-encoded JSON mapping schema names to SQL statement templates and JavaScript extractor functions |
| `authentication:username` | | | Overrides the credentials in the connection string |
| `authentication:password` | | | |
| `authentication:clientCertificate` | | | Base64 certificate. PostgreSQL mutual TLS only |
| `authentication:certificatePassword` | | | |

Statement parameters in a mapping are prefixed with `@`, and the extractor functions can call
`Guid()`, `DateTime()` and `TimeSpan()`.

### Resilience

This sink replaces the shared resilience block with its own, including a three-stage backoff.

| Key | Default | Meaning |
|---|---|---|
| `resilience:enabled` | `true` | |
| `resilience:commandTimeout` | `30` | Seconds |
| `resilience:connectionTimeout` | `15` | Seconds |
| `resilience:minPoolSize` | `0` | |
| `resilience:maxPoolSize` | `100` | |
| `resilience:connectionLifetime` | `0` | Seconds. `0` means no limit |
| `resilience:firstDelayBound:upperLimitMs` | `60000` | |
| `resilience:firstDelayBound:delayMs` | `5000` | |
| `resilience:secondDelayBound:upperLimitMs` | `3600000` | |
| `resilience:secondDelayBound:delayMs` | `600000` | |
| `resilience:thirdDelayBound:upperLimitMs` | `-1` | `-1` means retry forever |
| `resilience:thirdDelayBound:delayMs` | `3600000` | |

Everything in [settings every sink takes](/client/connectors/#settings-every-sink-takes) applies on
top of these.

Server reference:
[SQL sink](https://docs.kurrent.io/server/v26.1/features/connectors/sinks/sql).
