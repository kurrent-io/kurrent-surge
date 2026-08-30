---
title: Pulsar sink
description: Produce records to an Apache Pulsar topic.
---

`pulsar-sink` · **Licence required** · KurrentDB 25.1 and later

Produces each record to a Pulsar topic.

## Settings

| Key | Required | Default | Meaning |
|---|---|---|---|
| `topic` | yes | | Topic to produce to |
| `url` | | `pulsar://localhost:6650` | Service URL |
| `defaultHeaders` | | | Headers added to every message |
| `authentication:token` | | | JWT |
| `partitionKeyExtraction:enabled` | | `false` | |
| `partitionKeyExtraction:source` | | `partitionKey` | `stream`, `streamSuffix` or `headers` |
| `partitionKeyExtraction:expression` | | | A regex, or comma-separated header keys |
| `resilience:enabled` | | `true` | |
| `resilience:retryInterval` | | `00:00:03` | Seconds, or `HH:MM:SS` |

:::caution
This sink carries its own retry mechanism and does not use the shared `resilience:*` block. The two
`resilience:` keys above are its own.

The server documents `partitionKeyExtraction:source` as defaulting to `partitionKey`, which is not
among the values it lists as accepted. Set the value explicitly rather than relying on the default.
:::

Everything in [settings every sink takes](/client/connectors/#settings-every-sink-takes) applies on
top of these, except the shared resilience block.

Server reference:
[Pulsar sink](https://docs.kurrent.io/server/v26.1/features/connectors/sinks/pulsar).
