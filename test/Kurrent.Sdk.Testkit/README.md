# Kurrent.SDK.Testing

Shared test infrastructure for KurrentDB integration tests. Replaces the previous `Kurrent.Testing.Aspire` AppHost
and the `CommunityToolkit.Aspire.Hosting.KurrentDB` package with a single project that owns the full container
lifecycle.

## Contents

- [How it works](#how-it-works)
- [Project layout](#project-layout)
- [Quick start](#quick-start)
- [Usage examples](#usage-examples)
- [Hosting API reference](#hosting-api-reference)
- [Design decisions](#design-decisions)

## How it works

The project references `Aspire.AppHost.Sdk` so the DCP orchestrator path is stamped into its assembly metadata.
When `KurrentDBTestClusterBuilder.BuildAsync()` calls `DistributedApplicationTestingBuilder.Create()`, Aspire
walks the call stack to find that metadata. Because the call originates inside this assembly, DCP discovery
succeeds without a separate AppHost project.

At runtime the builder:

1. Creates an Aspire `DistributedApplication` with one or more `KurrentDBResource` containers.
2. Applies `KurrentDBConfiguration.Insecure` (or `.Secure`) environment variables from `Kurrent.Testing`.
3. Registers an HTTP health check that polls `GET /health/live` (no SDK client dependency).
4. Starts the app, waits for each resource to become healthy, extracts connection strings, and injects default
   admin credentials (`admin:changeit`).

## Project layout

```
Kurrent.SDK.Testing/
    Kurrent.SDK.Testing.csproj
    Hosting/
        KurrentDBResource.cs              Aspire container resource (TLS-aware connection string)
        KurrentDBContainerImageTags.cs    Registry/image/tag defaults (Latest, LTS, Preview)
        KurrentDBHealthCheck.cs           HTTP-based /health/live check
        KurrentDBBuilderExtensions.cs     AddKurrentDB() + WithSecureMode() + data volume/bind mount
        KurrentDBTestCluster.cs           Running cluster handle (connection strings + dispose)
        KurrentDBTestClusterBuilder.cs    Fluent builder for test scenarios
    Fixtures/
        KurrentDBTestFixture.cs           Ready-made TUnit fixture with KurrentClient
```

## Quick start

Add a project reference from your test project:

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <ProjectReference Include="..\Kurrent.SDK.Testing\Kurrent.SDK.Testing.csproj" />
</ItemGroup>
```

Use the fixture directly in a test class:

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

The fixture spins up a KurrentDB container on first use and shares it across all tests in the session.

## Usage examples

### Default single node (insecure)

```csharp
var cluster = await new KurrentDBTestClusterBuilder()
    .AddNode()
    .BuildAsync();

var connectionString = cluster.AuthenticatedConnectionString;
// kurrentdb://admin:changeit@localhost:{port}?tls=false

await cluster.DisposeAsync();
```

### Secure mode with TLS certificates

```csharp
var cluster = await new KurrentDBTestClusterBuilder()
    .AddNode(opts => {
        opts.Secure = true;
        opts.CertsPath = "/path/to/certs";
    })
    .BuildAsync();
```

### Multiple nodes

```csharp
var cluster = await new KurrentDBTestClusterBuilder()
    .AddNode("primary")
    .AddNode(opts => {
        opts.ResourceName = "secondary";
        opts.Secure = true;
        opts.CertsPath = "../../certs";
    })
    .BuildAsync();

var primaryCs   = cluster.GetConnectionString("primary");
var secondaryCs = cluster.GetConnectionString("secondary");
```

### Existing instance (skip containers)

```csharp
var cluster = await new KurrentDBTestClusterBuilder()
    .UseExistingInstance("kurrentdb://admin:changeit@localhost:2113?tls=false")
    .BuildAsync();
```

### Custom image

```csharp
var cluster = await new KurrentDBTestClusterBuilder()
    .AddNode(opts => {
        opts.Image = KurrentDBContainerImageTags.LTS.Image;
        opts.Tag = KurrentDBContainerImageTags.LTS.Tag;
    })
    .BuildAsync();
```

## Hosting API reference

### `KurrentDBBuilderExtensions`

| Method                       | Description                                                         |
|------------------------------|---------------------------------------------------------------------|
| `AddKurrentDB(name, image?)` | Add a KurrentDB container with insecure defaults. Image is optional |
| `WithSecureMode(certsPath)`  | Apply secure config + cert bind mount. Sets `tls=true`              |
| `WithDataVolume(name?)`      | Attach a named volume to `/var/lib/kurrentdb`                       |
| `WithDataBindMount(source)`  | Bind mount a host directory to `/var/lib/kurrentdb`                 |

### `KurrentDBTestClusterBuilder`

| Method                              | Description                                            |
|-------------------------------------|--------------------------------------------------------|
| `AddNode(name?)`                    | Add an insecure node with optional resource name       |
| `AddNode(Action<NodeOptions>)`      | Add a node with full configuration                     |
| `UseExistingInstance(connString)`   | Skip containers; use a running KurrentDB instance      |
| `BuildAsync(ct)`                    | Build, start, wait for health, return cluster handle   |

### `KurrentDBTestCluster`

| Member                               | Description                                          |
|--------------------------------------|------------------------------------------------------|
| `AuthenticatedConnectionString`      | Default node connection string with `admin:changeit` |
| `AnonymousConnectionString`          | Default node connection string without credentials   |
| `GetConnectionString(name)`          | Named node authenticated connection string           |
| `GetAnonymousConnectionString(name)` | Named node anonymous connection string               |
| `DisposeAsync()`                     | Stop and dispose the Aspire application              |

### `KurrentDBTestFixture`

| Member                      | Description                                                   |
|-----------------------------|---------------------------------------------------------------|
| `Client`                    | Pre-configured `KurrentClient` (no resilience, no validation) |
| `ConnectionString`          | Authenticated connection string for the default node          |
| `AnonymousConnectionString` | Anonymous connection string for the default node              |
| `Faker`                     | Shared `BogusFaker` instance (per test session)               |
| `Logger`                    | Test-scoped `ILogger`                                         |
| `LoggerFactory`             | Test-scoped `ILoggerFactory`                                  |
| `Time`                      | `FakeTimeProvider` for time-dependent tests                   |

### `KurrentDBContainerImageTags`

| Variant   | Image                           | Tag      |
|-----------|:--------------------------------|:---------|
| `Latest`  | `kurrent-latest/kurrentdb`      | `latest` |
| `LTS`     | `kurrent-lts/kurrentdb`         | `latest` |
| `Preview` | `kurrent-preview/kurrentdb`     | `latest` |

All images are pulled from `docker.kurrent.io`. Pass any `image:tag` to `AddKurrentDB()` or
`KurrentDBNodeOptions` to use a custom version.

## Design decisions

### HTTP health check instead of client-based

The CommunityToolkit health check created a `KurrentDBClient` and called `ReadAllAsync`. This pulled in the full
SDK as a dependency and could fail for reasons unrelated to server health (auth, schema, etc.). The replacement
uses a plain `HttpClient` to `GET /health/live`, which checks actual server readiness without SDK coupling.

### Environment variables reused from `Kurrent.Testing`

`KurrentDBConfiguration.Insecure` and `.Secure` in `Kurrent.Testing` define the canonical set of container
environment variables. `AddKurrentDB()` applies them via `WithEnvironment()` instead of duplicating the values.

### No hardcoded host ports

Aspire allocates host ports dynamically via `WithHttpEndpoint(targetPort: 2113)`. This avoids port conflicts
when running multiple test suites in parallel or on CI.

### TLS-aware connection strings

The original `KurrentDBResource` hardcoded `tls=false` in `ConnectionStringExpression`. The replacement reads
`Secure` from the resource to emit `tls=true` or `tls=false` as appropriate.

### DCP metadata via `Aspire.AppHost.Sdk`

`DistributedApplicationTestingBuilder.Create()` walks the call stack to find `dcpclipath` assembly metadata.
The `Aspire.AppHost.Sdk` reference stamps this metadata into `Kurrent.SDK.Testing.dll`. The `ASPIRE002` warning
about missing `Aspire.Hosting.AppHost` is suppressed because we use `Aspire.Hosting.Testing` instead.
