---
title: RabbitMQ sink
description: Publish records to a RabbitMQ exchange.
---

`rabbit-mq-sink` · **Licence required** · KurrentDB 24.10 and later

Publishes each record to a RabbitMQ exchange.

## Settings

| Key | Required | Default | Meaning |
|---|---|---|---|
| `exchange:name` | yes | | Exchange to publish to |
| `exchange:type` | | `fanout` | |
| `routingKey` | | `""` | |
| `host` | | `localhost` | |
| `port` | | `5672` | |
| `virtualHost` | | `/` | |
| `connectionName` | | the connector id | Shown in RabbitMQ's own logging and diagnostics |
| `durable` | | `true` | |
| `autoDelete` | | `false` | |
| `waitForBrokerAck` | | `false` | Wait for the broker to acknowledge before checkpointing |
| `authentication:username` | | `guest` | |
| `authentication:password` | | `guest` | |

### Resilience

This sink replaces the shared resilience block with its own.

| Key | Default | Meaning |
|---|---|---|
| `resilience:connectionTimeoutMs` | `60000` | |
| `resilience:handshakeTimeoutMs` | `10000` | |
| `resilience:requestedHeartbeatMs` | `60000` | |

Everything in [settings every sink takes](/client/connectors/#settings-every-sink-takes) applies on
top of these.

Server reference:
[RabbitMQ sink](https://docs.kurrent.io/server/v26.1/features/connectors/sinks/rabbitmq).
