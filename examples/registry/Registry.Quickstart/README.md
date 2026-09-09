# Registry.Quickstart

Three actors share a `TemperatureReading` schema:

- **Platform** registers the schema with backward compatibility
- **Ingestion** writes sensor readings, evolves the schema
- **Dashboard** reads and displays readings, unchanged after evolution

## Run

```bash
dotnet run --project examples/registry/Registry.Quickstart
```

Connects via `ConnectionStrings:kurrentdb` in `appsettings.json`, which defaults to
`localhost:2113`. Point at a different server by editing the file or setting the
`ConnectionStrings__kurrentdb` environment variable. Idempotent.

## Output

```
[platform]   Schema TemperatureReading already registered
[ingestion]  Wrote 3 readings
[dashboard]  sensor-1: 22.5 C
[dashboard]  sensor-2: 18.3 C
[dashboard]  sensor-1: 23.1 C
[ingestion]  Schema change blocked — MissingRequiredProperty at #/celsius
[ingestion]  Schema already evolved
[ingestion]  Wrote 1 readings with location
[dashboard]  sensor-1: 22.5 C
[dashboard]  sensor-2: 18.3 C
[dashboard]  sensor-1: 23.1 C
[dashboard]  sensor-3: 19.7 C (warehouse-b)
[platform]   TemperatureReading: Backward, version 2
```

## Flow

1. Platform registers v1 with backward compatibility
2. Ingestion writes, dashboard reads
3. Renaming `celsius` → `tempC` is blocked (breaks consumers)
4. Adding optional `location` is allowed
5. Dashboard reads old and new events without changes
