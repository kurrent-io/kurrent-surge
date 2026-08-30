---
title: Kafka sink
description: Write records to a Kafka topic.
---

`kafka-sink` · **Licence required** · KurrentDB 24.10 and later

Produces each record to a Kafka topic.

## Settings

| Key | Required | Default | Meaning |
|---|---|---|---|
| `topic` | yes | | The topic to produce to |
| `bootstrapServers` | | `localhost:9092` | Broker list |
| `defaultHeaders` | | | Headers added to every message |
| `authentication:securityProtocol` | | `plaintext` | `plaintext`, `saslPlaintext` or `saslSsl` |
| `authentication:saslMechanism` | | `plain` | `plain`, `scramSha256` or `scramSha512` |
| `authentication:username` | | | SASL username |
| `authentication:password` | | | SASL password |
| `partitionKeyExtraction:enabled` | | `false` | |
| `partitionKeyExtraction:source` | | `partitionKey` | `partitionKey`, `stream`, `streamSuffix` or `headers` |
| `partitionKeyExtraction:expression` | | | A regex, or comma-separated header keys |
| `waitForBrokerAck` | | `false` | Wait for the broker to acknowledge before checkpointing |
| `brokerAddressFamily` | | `V4` | `Any`, `V4` or `V6` |
| `compression:type` | | `Zstd` | `None`, `Gzip`, `Lz4`, `Zstd` or `Snappy` |
| `compression:level` | | `6` | |
| `resilience:reconnectBackoffMaxMs` | | `20000` | |
| `resilience:messageSendMaxRetries` | | `2147483647` | |

:::caution
This sink carries its own Kafka retry mechanism and **ignores** the shared `resilience:*` block.
Use the two `resilience:` keys in the table above instead.
:::

Everything in [settings every sink takes](/client/connectors/#settings-every-sink-takes) applies on
top of these, except the shared resilience block noted above.

A working configuration, with partition-key extraction and compression, is in
[`examples/processors/Connector.Management`](https://github.com/kurrent-io/kurrent-sdk-dotnet/tree/main/examples/processors/Connector.Management).

Server reference:
[Kafka sink](https://docs.kurrent.io/server/v26.1/features/connectors/sinks/kafka).
