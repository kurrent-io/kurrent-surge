---
title: Serilog sink
description: Write records through Serilog, to the console, a file or Seq.
---

`serilog-sink` · **No licence needed** · KurrentDB 24.10 and later

Logs each record through Serilog. With no configuration it writes to the server console, which makes
it the fastest way to see a connector working. The SDK's own integration tests use it for that
reason.

## Settings

| Key | Required | Default | Meaning |
|---|---|---|---|
| `configuration` | | unset | Base64-encoded Serilog JSON configuration. Supports the Console, File and Seq sinks. Unset writes to the console |
| `includeRecordData` | | `true` | Include the record's data in the log output |

This sink adds no resilience settings of its own and uses the shared block.

Everything in [settings every sink takes](/client/connectors/#settings-every-sink-takes) applies on
top of these.

Server reference:
[Serilog sink](https://docs.kurrent.io/server/v26.1/features/connectors/sinks/serilog).
