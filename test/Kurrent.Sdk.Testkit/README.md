# Kurrent.Sdk.Testkit

TUnit fixtures that spin up KurrentDB via Aspire and hand tests a ready-to-use `KurrentClient`.

## Project layout

```
test/
  Kurrent.Sdk.Testkit.Hosting/   Aspire resource + builder extensions for KurrentDB containers
  Kurrent.Sdk.Testkit.AppHost/   Aspire AppHost; reads --topology / --security args, calls AddKurrentDB
  Kurrent.Sdk.Testkit/           TUnit fixtures wired through TUnit.Aspire (this project)
```

The Hosting library is split out so the AppHost can reference it without circularity. The fixtures here reference the AppHost through the generated `Projects.Kurrent_Sdk_Testkit_AppHost` type and switch topology/security via `Args`.

## Fixtures

| Fixture                       | Topology               | Security |
|-------------------------------|------------------------|----------|
| `KurrentDBTestFixture`        | Single node            | Insecure |
| `KurrentDBSecureTestFixture`  | Single node            | TLS      |
| `KurrentDBClusterTestFixture` | 3 nodes                | TLS      |
| `KurrentClientTestFixture`    | Single node + seeding  | Insecure |

`KurrentClientTestFixture` extends `KurrentDBTestFixture` with `SeedTestStream`, `TestStreams`, `TypeMapper`, and the Arcade Racer test data source.

## Usage

```csharp
public class StreamTests {
    [ClassDataSource<KurrentDBTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBTestFixture Fixture { get; init; }

    [Test]
    public async ValueTask appends_events(CancellationToken ct) {
        await Fixture.Client.Streams.AppendRecords(..., ct);
    }
}
```

The fixture starts the AppHost on first use and shares it across all tests in the session. Each fixture variant is its own session — secure, insecure, and cluster do not share containers.

## Adding a new variant

Subclass `KurrentDBTestFixture` and override `Args`:

```csharp
public sealed class KurrentDBLargeClusterFixture : KurrentDBTestFixture {
    protected override string[] Args =>
    [
        "--topology:nodes=5",
        "--topology:readOnlyReplicas=2",
        "--security:tls=true"
    ];
}
```

The AppHost (`test/Kurrent.Sdk.Testkit.AppHost/Program.cs`) reads these keys via `IConfiguration` and feeds them to `KurrentDBTestOptions`.

## Debugging container failures

To stream a container's logs into a single failing test, use TUnit.Aspire's `WatchResourceLogs`:

```csharp
[Test]
public async ValueTask debugs_cluster_handshake(CancellationToken ct) {
    await using var _ = Fixture.WatchResourceLogs("kurrentdb-2");
    // test body
}
```

Resource state changes (`Starting -> Running -> Healthy`) are written to stderr during fixture initialization, so CI logs show progress instead of a black box. On timeout or `FailedToStart`, the relevant container's recent logs are included in the exception message automatically.

## TLS certificates

Secure fixtures spin up an `es-gencert-cli` container that runs once before any KurrentDB node starts. The generated cert directory is exposed via `Fixture.CertsDirectory` and removed on dispose.

One-time host setup: `sudo ./scripts/gencert.sh` (only required if running outside containers).
