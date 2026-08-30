---
title: MongoDB sink
description: Write records into a MongoDB collection.
---

`mongo-db-sink` · **Licence required** · KurrentDB 24.10 and later

Writes each record as a document in a MongoDB collection, in batches.

## Settings

| Key | Required | Default | Meaning |
|---|---|---|---|
| `database` | yes | | Target database |
| `collection` | yes | | Target collection |
| `connectionString` | yes | `mongodb://mongoadmin:secret@localhost:27017/admin` | MongoDB URI |
| `documentId:source` | | `recordId` | `recordId`, `stream`, `headers` or `streamSuffix` |
| `documentId:expression` | | | Expression that formats the document id |
| `certificate:rawData` | | `""` | Base64 X.509 certificate |
| `certificate:password` | | `""` | |
| `batching:batchSize` | | `1000` | Documents per batch |
| `batching:batchTimeoutMs` | | `250` | Flush a partial batch after this long |

Authenticate either with SCRAM credentials in the `connectionString`, or with an X.509 certificate
through the `certificate:` keys.

This sink adds no resilience settings of its own and uses the shared block.

Everything in [settings every sink takes](/client/connectors/#settings-every-sink-takes) applies on
top of these.

Server reference:
[MongoDB sink](https://docs.kurrent.io/server/v26.1/features/connectors/sinks/mongo).
