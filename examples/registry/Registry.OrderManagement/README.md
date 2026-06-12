# Registry.OrderManagement

An order management API showing how the schema registry governs event production. Events written to streams are
automatically registered in the registry and stamped with their schema version ID —
so every event in the log carries a traceable link back to its contract.

## Running the Example

```bash
cd examples/registry/Registry.OrderManagement
dotnet run
```

Requires KurrentDB running on `localhost:2113`. The API starts on `http://localhost:5000`.

## Try It

### 1. Place an order

The client auto-registers an `OrderPlaced` schema in the registry on first write.

```bash
curl -s http://localhost:5000/orders \
  -H 'Content-Type: application/json' \
  -d '{
    "customerId": "cust-42",
    "items": [
      { "sku": "WIDGET-001", "quantity": 2, "unitPrice": 29.99 },
      { "sku": "GADGET-X", "quantity": 1, "unitPrice": 49.99 }
    ]
  }' | jq
```

Note the `orderId` in the response.

### 2. Read events back — see schema metadata

Every event carries its schema name, format, and version ID.

```bash
curl -s http://localhost:5000/orders/ORDER_ID/events | jq
```

```json
[
  {
    "revision": 0,
    "schemaName": "order-placed",
    "schemaFormat": "Json",
    "schemaVersionId": "d4e5f6...",
    "clrType": "OrderPlaced"
  }
]
```

This is the registry's value: every event in the log is linked to a specific schema version.
Consumers know exactly what shape each event has.

### 3. Ship the order

```bash
curl -s http://localhost:5000/orders/ORDER_ID/ship \
  -H 'Content-Type: application/json' \
  -d '{ "carrier": "FedEx", "trackingNumber": "FX-123456" }' | jq
```

```bash
curl -s http://localhost:5000/orders/ORDER_ID/events | jq
# → Two events, each with their own schema name and version ID
```

### 4. Register a contract manually with compatibility rules

The auto-registered schemas use `None` compatibility. For production, register contracts with backward compatibility
so the registry enforces safe evolution.

```bash
curl -s http://localhost:5000/schemas \
  -H 'Content-Type: application/json' \
  -d '{
    "schemaName": "orders.order-placed-v1",
    "definition": "{\"type\":\"object\",\"required\":[\"orderId\",\"customerId\",\"items\",\"total\",\"placedAt\"],\"properties\":{\"orderId\":{\"type\":\"string\"},\"customerId\":{\"type\":\"string\"},\"items\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"required\":[\"sku\",\"quantity\",\"unitPrice\"],\"properties\":{\"sku\":{\"type\":\"string\"},\"quantity\":{\"type\":\"integer\",\"minimum\":1},\"unitPrice\":{\"type\":\"number\",\"exclusiveMinimum\":0}}},\"minItems\":1},\"total\":{\"type\":\"number\",\"exclusiveMinimum\":0},\"placedAt\":{\"type\":\"string\",\"format\":\"date-time\"}}}",
    "description": "Customer placed an order",
    "compatibility": "Backward",
    "domain": "orders",
    "team": "orders-team"
  }' | jq
```

### 5. Catch a breaking change

Rename `customerId` to `buyerId` — the registry rejects it because removing a required field
breaks consumers reading historical events.

```bash
curl -s http://localhost:5000/schemas/orders.order-placed-v1/check \
  -H 'Content-Type: application/json' \
  -d '{
    "definition": "{\"type\":\"object\",\"required\":[\"orderId\",\"buyerId\",\"items\",\"total\",\"placedAt\"],\"properties\":{\"orderId\":{\"type\":\"string\"},\"buyerId\":{\"type\":\"string\"},\"items\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"required\":[\"sku\",\"quantity\",\"unitPrice\"],\"properties\":{\"sku\":{\"type\":\"string\"},\"quantity\":{\"type\":\"integer\",\"minimum\":1},\"unitPrice\":{\"type\":\"number\",\"exclusiveMinimum\":0}}},\"minItems\":1},\"total\":{\"type\":\"number\",\"exclusiveMinimum\":0},\"placedAt\":{\"type\":\"string\",\"format\":\"date-time\"}}}"
  }' | jq
# → isCompatible: false, with errors explaining what broke
```

### 6. Evolve safely

Add optional `shippingAddress` and `discountCode` fields. Optional fields are backward-compatible.

```bash
curl -s http://localhost:5000/schemas/orders.order-placed-v1/versions \
  -H 'Content-Type: application/json' \
  -d '{
    "definition": "{\"type\":\"object\",\"required\":[\"orderId\",\"customerId\",\"items\",\"total\",\"placedAt\"],\"properties\":{\"orderId\":{\"type\":\"string\"},\"customerId\":{\"type\":\"string\"},\"items\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"required\":[\"sku\",\"quantity\",\"unitPrice\"],\"properties\":{\"sku\":{\"type\":\"string\"},\"quantity\":{\"type\":\"integer\",\"minimum\":1},\"unitPrice\":{\"type\":\"number\",\"exclusiveMinimum\":0}}},\"minItems\":1},\"total\":{\"type\":\"number\",\"exclusiveMinimum\":0},\"placedAt\":{\"type\":\"string\",\"format\":\"date-time\"},\"shippingAddress\":{\"type\":\"string\"},\"discountCode\":{\"type\":\"string\"}}}"
  }' | jq
# → versionNumber: 2
```

### 7. Resolve a version by ID (event replay)

During replay, consumers use the version ID from event metadata to look up the exact schema definition.

```bash
curl -s http://localhost:5000/schemas/resolve/VERSION_ID_HERE | jq
```

### 8. Discover schemas

```bash
# All schemas
curl -s 'http://localhost:5000/schemas' | jq

# By domain prefix
curl -s 'http://localhost:5000/schemas?prefix=orders.' | jq

# By team ownership
curl -s 'http://localhost:5000/schemas?team=orders-team' | jq
```

### 9. Clean up

```bash
curl -s http://localhost:5000/schemas/orders.order-placed-v1 -X DELETE | jq
```

## Endpoints

| Method | Path | What It Does |
|--------|------|-------------|
| POST | `/orders` | Place an order (auto-registers schema, stamps version ID) |
| POST | `/orders/{id}/ship` | Ship an order |
| GET | `/orders/{id}/events` | Event history with schema metadata per event |
| POST | `/schemas/seed` | Bootstrap order schemas with compatibility rules |
| POST | `/schemas` | Register a contract manually |
| POST | `/schemas/{name}/versions` | Evolve a schema |
| POST | `/schemas/{name}/check` | Compatibility gate |
| GET | `/schemas/{name}` | Schema metadata |
| GET | `/schemas/{name}/versions/{n}` | Full definition for a version |
| GET | `/schemas/resolve/{versionId}` | Resolve version by ID |
| GET | `/schemas` | Discover (`?prefix=`, `?team=`) |
| DELETE | `/schemas/{name}` | Delete a schema |

## Key Code

### Client configured for auto-registration

```csharp
builder.Services.AddKurrentClient(() =>
    KurrentClientOptions.Parse("kurrentdb://...") with {
        Schema = new KurrentClientSchemaOptions {
            Json = new JsonSchemaSerializerOptions {
                SchemaRegistration = SchemaRegistrationOptions.AutoRegister
            }
        }
    });
```

### Events stamped with schema metadata on write

```csharp
// The client serializes the event, auto-registers its JSON schema,
// and stamps the record with schema name + version ID
await client.Streams.AppendStream(
    $"order-{orderId}",
    [AppendRecord.New.WithValue(new OrderPlaced(...)).Build()]);
```

### Schema metadata visible on read

```csharp
await foreach (var msg in stream) {
    msg.Switch(
        record => {
            record.Schema.SchemaName;      // "order-placed"
            record.Schema.SchemaFormat;     // Json
            record.Schema.SchemaVersionId;  // guid linking to registry
        },
        _ => { });
}
```
