# Kurrent.Sdk.Testkit

TUnit fixtures that spin up KurrentDB via Aspire and hand tests a ready-to-use `KurrentClient`.

## Project layout

```
test/
  Aspire.Hosting.KurrentDB/       Aspire resource definitions, builder extensions, health checks
  Aspire.Hosting.KurrentDB.Tests/ Tests for the hosting library
  Kurrent.Sdk.Testkit/            TUnit fixtures using inline Aspire resource registration (this project)
```

This project uses `Aspire.AppHost.Sdk` with `OutputType=Library` — no `Program.cs` needed. Resources are registered inline via `DistributedApplicationTestingBuilder.Create()`, not through an AppHost project reference.

## Fixtures

| Fixture                   | Topology    | Security |
|---------------------------|-------------|----------|
| `KurrentDBFixture`        | Single node | Insecure |
| `KurrentDBSecureFixture`  | Single node | TLS      |
| `KurrentDBClusterFixture` | 3 nodes     | TLS      |

`KurrentDBFixture` provides `Client`, `ConnectionString`, `SeedTestStream`, `TestStreams`, `TypeMapper`, and the Arcade Racer test data source.

### Base classes

- `AspireFixtureBase` — manages the `DistributedApplication` lifecycle (build, start, wait for resources, teardown)
- `AspireFixture` — inline resource registration via `DistributedApplicationTestingBuilder.Create()`
- `AspireFixture<T>` — AppHost project reference via `DistributedApplicationTestingBuilder.CreateAsync<T>()`

## Usage

```csharp
public class StreamTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; init; }

    [Test]
    public async ValueTask appends_events(CancellationToken ct) {
        await Fixture.Client.Streams.AppendStream(..., ct);
    }
}
```

The fixture starts the Aspire app on first use and shares it across all tests in the session. Each fixture variant is its own session — secure, insecure, and cluster do not share containers.

## Adding a new variant

Subclass `KurrentDBFixture` and override `Settings`:

```csharp
public sealed class KurrentDBLargeClusterFixture : KurrentDBFixture {
    KurrentDBLargeClusterFixture() =>
        Settings = KurrentDBSettings.DefaultCluster;
}
```

## TLS certificates

One-time host setup: `sudo ./scripts/gencert.sh` (only required if running outside containers).
