# Connector.Management

Kafka sink connector that streams payment plan events from KurrentDB to Apache Kafka.

## What You'll Learn

- Provisioning a connector with configuration from `appsettings.json`
- Idempotent create-or-reconfigure pattern for safe redeployments
- Using `IConfiguration.ToProtobufStruct()` to load hierarchical connector settings
- Writing events to streams that a sink connector consumes
- Monitoring connector health via API endpoints
- Graceful connector lifecycle management (start on boot, stop on shutdown)

## How It Works

1. **Configuration**: Connector settings (Kafka bootstrap servers, topic, stream filters,
   compression, partition key extraction) are loaded from `appsettings.json`
2. **Provisioning**: On startup, the app creates the connector or reconfigures it if it
   already exists — safe for repeated deployments
3. **Event production**: A background service writes `PaymentPlanCreated` and `PaymentCollected`
   events to `paymentPlan-*` streams every few seconds
4. **Sinking**: The Kafka sink connector picks up events matching the `paymentPlan-` prefix
   and pushes them to the configured Kafka topic
5. **Monitoring**: `/health` returns 200 when the connector is running, 503 otherwise;
   `/status` returns detailed connector info including settings and position

## Prerequisites

- KurrentDB running on `localhost:2113` **with a licence that includes the Kafka sink connector**.
  On a community edition server `Create` fails with
  `FailedPrecondition: Usage of the KafkaSink connector is not authorized`.
- Apache Kafka broker running on `localhost:9092`

## Running the Example

Start Kafka (KRaft mode, no Zookeeper):

```bash
cd examples/Connector.Management
docker compose up -d
```

Run the app:

```bash
dotnet run
```

## Endpoints

| Endpoint  | Description                                    |
|-----------|------------------------------------------------|
| `/health` | Returns 200 if connector is running, 503 if not |
| `/status` | Returns detailed connector state and settings   |

## Configuration

All connector settings live in `appsettings.json` under the `Connector` section:

```json
{
  "Connector": {
    "Id": "kafka-sink-payments",
    "Settings": {
      "instanceTypeName": "kafka-sink",
      "bootstrapServers": "localhost:9092",
      "topic": "payments",
      "subscription": {
        "filter": {
          "scope": "stream",
          "filterType": "prefix",
          "expression": "paymentPlan-"
        }
      }
    }
  }
}
```

Override any value with environment variables:

```bash
Connector__Settings__bootstrapServers=kafka.prod:9092 dotnet run
```

## Sample Output

```
[12:00:01 INF] Created connector kafka-sink-payments
[12:00:01 INF] Connector kafka-sink-payments started
[12:00:03 INF] Payment plan service started — writing events to paymentPlan-* streams
[12:00:03 INF] Wrote PaymentPlanCreated to paymentPlan-pp-0001 (USD 1)
[12:00:03 INF] Wrote PaymentCollected to paymentPlan-pp-0001
[12:00:06 INF] Wrote PaymentPlanCreated to paymentPlan-pp-0002 (EUR 2)
[12:00:09 INF] Wrote PaymentPlanCreated to paymentPlan-pp-0003 (GBP 3)
[12:00:09 INF] Wrote PaymentCollected to paymentPlan-pp-0003
```
