---
title: Elasticsearch sink
description: Index records into an Elasticsearch index.
---

`elasticsearch-sink` · **Licence required** · KurrentDB 25.0 and later

Indexes each record into Elasticsearch, in batches.

## Settings

| Key | Required | Default | Meaning |
|---|---|---|---|
| `url` | yes | `http://localhost:9200` | Cluster URL |
| `indexName` | yes | `""` | Index to write to |
| `refresh` | | `true` | `true`, `false` or `wait_for`, controlling shard refresh |
| `batching:batchSize` | | `1000` | Records per batch |
| `batching:batchTimeoutMs` | | `250` | Flush a partial batch after this long |
| `documentId:source` | | `recordId` | `recordId`, `stream`, `headers` or `streamSuffix` |
| `documentId:expression` | | `""` | Expression that formats the document id |

### Authentication

| Key | Default | Meaning |
|---|---|---|
| `authentication:method` | `basic` | `basic`, `token` or `apiKey` |
| `authentication:username` | `elastic` | |
| `authentication:password` | `changeme` | |
| `authentication:apiKey` | `""` | Used when the method is `apiKey` |
| `authentication:base64ApiKey` | `""` | Used when the method is `token` |
| `authentication:clientCertificate:rawData` | `""` | Base64 X.509 client certificate, for mutual TLS |
| `authentication:clientCertificate:password` | `""` | |
| `authentication:rootCertificate:rawData` | `""` | Base64 X.509 root certificate |
| `authentication:rootCertificate:password` | `""` | |

### Resilience

This sink replaces the shared resilience block with its own.

| Key | Default | Meaning |
|---|---|---|
| `resilience:enabled` | `true` | |
| `resilience:connectionLimit` | `80` | Maximum concurrent connections |
| `resilience:maxRetries` | `2147483647` | |
| `resilience:maxRetryTimeout` | `60` | Milliseconds |
| `resilience:requestTimeout` | `60000` | Milliseconds |
| `resilience:dnsRefreshTimeout` | `300000` | Milliseconds |
| `resilience:pingTimeout` | `120000` | Milliseconds |
| `resilience:retryOnErrorTypes` | `""` | Comma-separated error types |

Everything in [settings every sink takes](/client/connectors/#settings-every-sink-takes) applies on
top of these.

Server reference:
[Elasticsearch sink](https://docs.kurrent.io/server/v26.1/features/connectors/sinks/elasticsearch).
