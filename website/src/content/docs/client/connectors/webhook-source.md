---
title: Webhook source
description: Receive HTTP webhooks and write them into KurrentDB streams.
---

`webhook-source` · **Licence unstated** · KurrentDB 26.1 and later

Accepts `POST /webhook/{connectorId}` and writes each request into a stream. A JavaScript routing
script decides which stream. The ingest endpoint is separate from the connector management API this
SDK talks to.

Its documentation page carries no licence badge, unlike every other licensed connector. Absence of a
badge is not a statement either way, so confirm before you depend on it.

## Settings

| Key | Required | Default | Meaning |
|---|---|---|---|
| `routingScript` | yes | | Base64-encoded JavaScript defining `route(request) => { stream, schema? }` |
| `waitForWrite` | | `false` | Respond only once the write is durable |
| `confirmationTimeout` | | `00:00:30` | Maximum wait when `waitForWrite` is on |
| `allowedHeaders` | | `Content-Type`, `User-Agent`, `X-Request-Id` | Inbound headers kept as record metadata |

### Signature validation

Omit the whole block and validation is off.

| Key | Required | Default | Meaning |
|---|---|---|---|
| `signature:scheme` | with the block | | `GitHub`, `Shopify`, `Slack` or `Stripe` |
| `signature:secret` | with the block | | HMAC shared secret, UTF-8 |
| `signature:timestampTolerance` | | `00:05:00` | Clock-skew tolerance. Used by the Slack and Stripe schemes only |

:::caution
Do not put authorization or cookie headers in `allowedHeaders`. They would be persisted into the
record's metadata, where they outlive the request.
:::

### Responses

`202 Accepted` by default, `201 Created` when `waitForWrite` is on, and `504` when that wait times
out. `400`, `401`, `404`, `415`, `500` and `503` carry their usual meanings.

A source takes `instanceTypeName` and `logging:enabled` and nothing else from the shared set.

Server reference:
[Webhook source](https://docs.kurrent.io/server/v26.1/features/connectors/sources/webhook).
