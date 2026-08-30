---
title: Kafka source
description: Consume a Kafka topic into KurrentDB streams.
---

`kafka-source` · **Licence required** · KurrentDB 26.0 and later

Consumes a Kafka topic and writes each message into KurrentDB. The consumer group is named
`kurrentdb-{ConnectorId}`.

## Settings

| Key | Required | Default | Meaning |
|---|---|---|---|
| `topic` | yes | | Topic to consume |
| `schemaName` | yes | | Schema name tagged onto every message written |
| `partition` | | | A specific partition. Blank consumes all of them |
| `offset` | | | Starting offset |
| `preserveMagicByte` | | `false` | Keep the Kafka schema-registry magic byte |
| `concurrency:channelCapacity` | | `10000` | Bounded channel capacity per consumer |
| `concurrency:tasks` | | `1` | Parallel consumer tasks |

### Consumer

| Key | Default | Meaning |
|---|---|---|
| `consumer:bootstrapServers` | `localhost:9092` | |
| `consumer:autoOffsetReset` | `Earliest` | |
| `consumer:enableAutoCommit` | `true` | |
| `consumer:autoCommitIntervalMs` | `5000` | |
| `consumer:sessionTimeoutMs` | `45000` | |
| `consumer:enablePartitionEof` | `true` | |
| `consumer:securityProtocol` | `plaintext` | `plaintext`, `ssl` or `saslPlaintext` |
| `consumer:saslMechanism` | `""` | `plain` |
| `consumer:saslUsername` | | |
| `consumer:saslPassword` | | |
| `consumer:sslCaPem` | | PEM |
| `consumer:sslCertificatePem` | | PEM |
| `consumer:sslKeyPem` | | PEM |

### Where messages land

| Key | Default | Meaning |
|---|---|---|
| `stream:strategy` | `topic` | `topic`, `partitionKey`, `fixed` or `header` |
| `stream:expression` | | Routing expression. Its meaning depends on the strategy |

:::caution
A source is not a sink with the arrow reversed. It takes `instanceTypeName` and `logging:enabled`
and nothing else from the shared set, so the subscription, transformer, resilience and autocommit
blocks do not apply.
:::

Server reference:
[Kafka source](https://docs.kurrent.io/server/v26.1/features/connectors/sources/kafka).
