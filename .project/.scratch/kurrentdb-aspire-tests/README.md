# KurrentDB Client Integration Tests

Integration tests for the KurrentDB .NET client using .NET Aspire for container orchestration.

## Workflows

### CI / self-contained mode

No prerequisites other than Docker. Spins up KurrentDB, runs tests, tears everything down.

```bash
dotnet test
```

### Local dev with reuse

Start the infrastructure once, keep it running, iterate fast on tests.

**Terminal 1** — start the test infrastructure:

```bash
dotnet run --project test/KurrentDB.TestHost
```

This gives you the Aspire dashboard and the KurrentDB Admin UI. Both URLs are printed to the console.

**Terminal 2** — run tests against the running instance:

```bash
ConnectionStrings__kurrentdb="kurrentdb://localhost:2113?tls=false" dotnet test
```

Replace the port with whatever the TestHost assigned if you didn't fix it.

Tests use unique stream names (`test-{guid}`) so previous test data doesn't interfere.

### Fixing the port for reuse

If you want a stable URL for the KurrentDB UI during local dev, pin the port in `Program.cs`:

```csharp
builder.AddKurrentDB("kurrentdb", httpPort: 2113);
```

## Project structure

```
test/
  KurrentDB.TestHost/          # Aspire AppHost — container orchestrator
    Program.cs                 # AddKurrentDB + future infra (Kafka, Redis, etc.)
  KurrentDB.Client.Tests/      # xUnit test project
    KurrentDBFixture.cs        # shared fixture, boots AppHost once per collection
    AppendToStreamTests.cs     # example tests
```

## Adding more infrastructure

Edit `test/KurrentDB.TestHost/Program.cs`:

```csharp
if (string.IsNullOrEmpty(builder.Configuration["ConnectionStrings:kafka"])) {
    builder.AddKafka("kafka");
} else {
    builder.AddConnectionString("kafka");
}
```

Same pattern — container when nothing's provided, passthrough when it is.
