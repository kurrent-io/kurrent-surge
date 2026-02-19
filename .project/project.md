# KurrentDB .NET Client - AI Agent Development Instructions

**Version:** v1.1.0
**Last Updated:** January 17, 2026
**Personnel:** SILVEIRA

---

## Project Overview

Kurrent .NET Client is an enterprise-grade client library for .NET. 
It provides type-safe, high-performance access to KurrentDB event streaming database with sophisticated features including schema registry integration, dual protocol support, and advanced resilience patterns.

Kurrent is a **platform**, not a database. 

The SDK is a .NET monorepo with separate NuGet packages (no meta-package). 

Always refer to the KDD naming decision document and existing README for product positioning before writing any public-facing copy.

**Core Principles:**
- **Type-safe error handling** – Result pattern throughout, no exception-based flow control
- **Functional programming influence** – Discriminated unions, Result monads, immutability
- **Zero reflection** – Source generators for AOT compatibility
- **Protocol versioning** – Dual support for v1 (EventStoreDB compatibility) and v2 (KurrentDB native)
- **Schema governance** - First-class schema registry integration with validation

**Technology Stack:**
- Primary: C# 14, .NET 8.0/9.0/10.0
- Communication: gRPC, falling back to HTTP/2
- Serialization: Protocol Buffers, JSON
- Resilience: Polly, Microsoft.Extensions.Http.Resilience
- Testing: TUnit (NOT xUnit/NUnit), FluentDocker
- Observability: OpenTelemetry, Serilog

---

## Architecture & Structure

### Modular Layered Architecture with DDD

```
Client Application
    ↓
KurrentClient (entry point)
    ↓
Feature Modules (7 domains)
    ├─ StreamsClient                    (Read/Append/Subscribe/Manage)
    ├─ RegistryClient                   (Schema management)
    ├─ ProjectionsClient                (Query/Management) [PENDING REFACTOR]
    ├─ PersistentSubscriptionsClient    (Consumer groups) [PENDING REFACTOR]
    ├─ UsersClient                      (Auth/Authz) [PENDING REFACTOR]
    ├─ ConnectorsClient                 (Data integration) [PENDING REFACTOR]
    └─ AdminClient                      (Server operations) [PENDING REFACTOR]
    ↓
Infrastructure Layer
    ├─ KurrentDBLegacyCallInvoker (gRPC invocation)
    ├─ LegacyClusterClient (cluster discovery)
    ├─ Protocol Buffers v1/v2 (serialization)
    └─ Error Handling (KurrentException + KurrentErrorDetails)
    ↓
Utility Layer
    ├─ Variant (Result types, discriminated unions)
    ├─ SchemaSerializerProvider (JSON/Protobuf/Avro)
    └─ MessageTypeMapper (CLR type → event type)
```

**Module Pattern:** All client modules inherit from `ClientModuleBase` which provides:
- Logger access
- SerializerProvider access
- ServerCapabilities access
- TypeMapper access
- MetadataDecoder access

### Source Code Organization

**Main Projects:**
- `src/KurrentDB.Client/Kurrent.Client/` - Main client library
- `src/Kurrent.Processors/` - Record processor framework
- `src/Kurrent.Client.SourceGenerators/` - Custom source generators
- `src/Variant/` - Result types and discriminated unions library
- `src/Variant.SourceGenerators/` - Variant code generation

**Key Directories:**
```
src/KurrentDB.Client/Kurrent.Client/
├── ConnectionString/          # Connection string parsing
├── Errors/                    # Structured error handling
├── Modules/                   # Feature modules (Streams, Registry, etc.)
├── Options/                   # Configuration and builders
└── Schema/                    # Schema management and serialization

src/Kurrent.Processors/
├── Checkpointing/             # Position tracking and checkpoints
├── Gateway/                   # Server communication
├── Options/                   # Processor configuration
├── Partitioning/              # Partition assignment strategies
└── State/                     # State store abstractions
```

---

## Development Conventions

### Project Structure Patterns

**Namespace Strategy:**
```csharp
// Feature-based namespaces (NOT folder-based)
// File: src/.../Modules/Streams/StreamsClient.cs
namespace Kurrent.Client.Streams;  // NOT Kurrent.Client.Modules.Streams

// File: src/.../Modules/Registry/RegistryClient.cs
namespace Kurrent.Client.Registry;  // NOT Kurrent.Client.Modules.Registry
```

**Partial Class Organization:**
```csharp
// Operations split into focused partials
StreamsClient.cs                              # Base definition
StreamsClient.Append.cs                       # Append operation
StreamsClient.Append.Models.cs                # Append DTOs
StreamsClient.Append.Mappers.cs               # Protocol mapping
StreamsClient.Append.Extensions.cs            # Extension methods
StreamsClient.Append.IdiomaticExtensions.cs   # Idiomatic API extensions
StreamsClient.Append.Models.Builders.cs       # Builder patterns
```

**Models Co-location:**
- Simple operations: Models in `*.Models.cs` next to operation
- Complex operations: Models in `Models/` subdirectory

### Code Style (NON-NEGOTIABLE)

1. **K&R Brace Style**
    ```csharp
    // CORRECT
    public class Foo {
        public void Bar() {
            if (condition) {
                // code
            }
        }
    }
    
    // WRONG - Allman style
    public class Foo
    {
        public void Bar()
        {
            // code
        }
    }
    ```

2. **Indentation**
   - C#: 4 spaces
   - Markup (XML, JSON, YAML): 2 spaces

3. **ConfigureAwait(false) EVERYWHERE in library code**
    ```csharp
    // REQUIRED in library code
    var result = await SomeOperation().ConfigureAwait(false);
    
    // Only omit in test code or application code
    ```

4. **Non-nullable references by default**
    ```csharp
    // Nullable reference types enabled in Directory.Build.props
    <Nullable>enable</Nullable>
    
    // Use ? for nullable
    public string? OptionalValue { get; set; }
    public string RequiredValue  { get; set; }  // Never null
    ```

5. **Test method naming: snake_case**
    ```csharp
    // CORRECT
    [Test]
    public async ValueTask registers_initial_version_of_new_schema(CancellationToken ct) { }
    
    // WRONG - PascalCase
    [Test]
    public async ValueTask RegistersInitialVersionOfNewSchema(CancellationToken ct) { }
    ```

### Custom Conventions

**Result Pattern (NO Exceptions for Flow Control):**
```csharp
// ALL public API methods return Result<TValue, TError>
public async ValueTask<Result<AppendSessionSuccess, AppendSessionError>>
    AppendSession(IAsyncEnumerable<AppendStreamRequest> requests, CancellationToken ct)

// Consumers use pattern matching
var result = await client.Streams.AppendSession(requests, ct);
return result.Match(
    success => ProcessSuccess(success),
    error => error switch {
        StreamNotFoundException notFound => HandleMissing(notFound),
        StreamRevisionConflictException conflict => HandleConflict(conflict),
        _ => throw error.AsException()  // Only as last resort
    }
);
```

**Discriminated Unions via IVariant:**
```csharp
// Define variant types for sum types
public readonly partial record struct ReadMessage : IVariant<Record, Heartbeat> { }

// Usage with switch expressions
ReadMessage msg = ...;
msg.Switch(
    record => ProcessRecord(record),
    heartbeat => HandleHeartbeat(heartbeat)
);
```

**Error Handling Attributes:**
```csharp
// Source generator creates exception from attribute
[KurrentOperationError(ServerError.StreamNotFound)]
public partial class StreamNotFoundException;

[KurrentOperationError(ServerError.NotLeaderNode)]
public partial class NotLeaderNodeException {
    public NodeInfo CurrentLeader { get; private set; } = NodeInfo.None;

    // Customize error extraction
    static NotLeaderNodeException() => OnInstanceCreated = (status, self) => {
        var details = status.GetDetail<NotLeaderNodeErrorDetails>()!;
        self.CurrentLeader = NodeInfo.From(
            details.CurrentLeader.Host,
            details.CurrentLeader.Port,
            details.CurrentLeader.NodeId);
    };
}
```

**Metadata Fluent API with Locking:**
```csharp
// Metadata can be locked for immutability
var metadata = Metadata.New
    .With("key1", "value1")
    .With("correlationId", Guid.NewGuid())
    .Lock();  // Now immutable

// Create unlocked copy if modifications needed
var modified = metadata.CreateUnlockedCopy()
    .With("newKey", "newValue");
```

**Builder Pattern via Static Property:**
```csharp
// Builders exposed via static New property/method
public record AppendStreamRequest {
    public static AppendStreamRequestBuilder New() => new();
}

// Usage
var request = AppendStreamRequest.New()
    .ForStream("orders")
    .WithRecord(r => r.WithValue(order).WithType("OrderPlaced"))
    .ExpectingStreamRevision(42)
    .Build();
```

---

## Testing Infrastructure

### TUnit Framework (NOT xUnit/NUnit!)

**Critical: Use `dotnet run`, NOT `dotnet test`**

### Run Tests

- always run for dotnet 8.0 until the tests pass.
- once tests pass for dotnet 8.0, run for dotnet 9.0 and 10.0 as well.

```bash
# CORRECT - TUnit execution
dotnet run \
--project test/Kurrent.Client.Integration.Tests/ \
--disable-logo --no-ansi --no-progress --fail-fast --adaptive-metrics \
--output Detailed --log-level Trace \
--framework net8.0

# Run specific category
dotnet run \
--project test/Kurrent.Client.Integration.Tests/ \
--disable-logo --no-ansi --no-progress --fail-fast --adaptive-metrics \
--output Detailed --log-level Trace \
--framework net8.0 \
--treenode-filter "/*/*/*/*[Category=Streams]"

# Run specific test class
dotnet run --project test/Kurrent.Client.Integration.Tests/ \
--disable-logo --no-ansi --no-progress --fail-fast --adaptive-metrics \
--output Detailed --log-level Trace \
--framework net8.0 \
--treenode-filter "/*/*/ReadTests/*"
```

### Test Fixtures Pattern

```csharp
// Base test fixture with DI
public class TestFixture : ITestStartEventReceiver, ITestEndEventReceiver {
    protected ILogger          Logger          { get; private set; }
    protected FakeTimeProvider TimeProvider    { get; private set; }
    protected IServiceProvider ServiceProvider { get; private set; }

    // Override to configure DI
    public virtual ValueTask OnSetUp(IServiceCollection services) =>
        ValueTask.CompletedTask;
}

// Integration test fixture
public class KurrentClientTestFixture : TestFixture {
    protected KurrentClient AutomaticClient { get; private set; }

    public override async ValueTask OnSetUp(IServiceCollection services) {
        services.AddSingleton(_ => Container.GetClient());
        await base.OnSetUp(services);
    }
}

// Test class usage
public class CreateSchemaTests : KurrentClientTestFixture {
    [Test, Timeout(10_000)]
    public async Task registers_initial_version_of_new_schema(CancellationToken ct) {
        // AutomaticClient available from fixture
        var result = await AutomaticClient.Registry.CreateSchema(...);
        // Test using custom Result assertions
        await result.ShouldNotThrowOrFailAsync(version => {
            version.VersionId.ShouldBeGuid();
            version.VersionNumber.ShouldBe(1);
        });
    }
}
```

### Docker Container Management

**Uses FluentDocker (NOT TestContainers):**

```csharp
// Custom container pattern with reuse
public class KurrentDBTestContainer : TestContainer {
    protected override ContainerBuilder ConfigureContainer(ContainerBuilder builder) {
        return builder
            .UseImage(TestContainerImage)
            .ExposePort(2113, 2113)
            .KeepContainer().KeepRunning().ReuseIfExists()  // Container reuse!
            .WaitUntilReadyWithConstantBackoff(1_000, 60, ...);
    }
}
```

**Container Configuration:**
From test/Kurrent.Testing/appsettings.json

```json
{
  "TestContainers": {
    "KurrentDB": {
      "Image": "docker.kurrent.io/kurrent-latest/kurrentdb:latest",
      "Enabled": true
    }
  }
}
```

### Custom Test Assertions

```csharp
// Result-based assertions (extension methods)
await operation
    .ShouldNotThrowOrFailAsync(value => {
        value.SomeProperty.ShouldBe(expected);
    });

await operation
    .ShouldFailAsync(error => {
        error.Case.ShouldBe(ExpectedErrorCase.NotFound);
    });
```

---

## Key Development Workflows

### Build Workflow

```bash
# Standard build (all frameworks: net8.0, net9.0, net10.0)
dotnet build

# Build specific framework
dotnet build --framework net10.0

# Build main client
dotnet build src/KurrentDB.Client/KurrentDB.Client.csproj

# Clean build
dotnet clean && dotnet build
```

**Build Process Includes:**
1. Protocol buffer compilation (automatic via Grpc.Tools)
2. Source generator execution (KurrentOperationErrorGenerator, Variant generators)
3. Multi-framework compilation

**Configuration:**
- `Directory.Build.props` - Root build settings (frameworks, language version, preview features) & Package metadata, MinVer versioning
- `Directory.Packages.props` - Central package management

### Test Execution Workflow

```bash
# 1. ONE-TIME SETUP: Generate certificates
sudo ./gencert.sh

# 2. Start observability infrastructure
cd test/Kurrent.Testing
docker compose up -d  # Starts Seq + Aspire Dashboard

# 3. Run tests (see TUnit Framework section above for full syntax)
dotnet run --project test/Kurrent.Client.Integration.Tests/Kurrent.Client.Integration.Tests.csproj --framework net10.0

# 4. View logs
# Seq: http://localhost:5341
# Aspire Dashboard: http://localhost:18888
```

### Debugging Workflow

**Local Debugging Setup:**

1. **Start supporting services:**
   - Seq (structured logs): http://localhost:5341
   - Aspire Dashboard (telemetry): http://localhost:18888
   - OTLP endpoint: port 4317
   ```bash
   cd test/Kurrent.Testing
   docker compose up -d
   ```

2. **Configure logging in appsettings.json:**
    ```json
    {
      "Serilog": {
        "MinimumLevel": {
          "Default": "Verbose",
          "Override": {
            "Grpc.Net.Client.Internal": "Error"
          }
        },
        "WriteTo": [
          { "Name": "Seq", "Args": { "serverUrl": "http://localhost:5341" } }
        ]
      }
    }
    ```

3. **Debug source generators:**
    ```csharp
    // Generators include debugger launch
    #if DEBUG
    if (!Debugger.IsAttached) Debugger.Launch();
    #endif
    ```

Set configuration to Debug, build project, and debugger will launch.

### Package Creation Workflow

```bash
# Get current version (MinVer automatic versioning)
dotnet tool restore
dotnet tool run minver -- --tag-prefix=v

# Create packages
dotnet pack --configuration Release

# Inspect package
./tools/inspect_nuget.sh src/KurrentDB.Client/bin/Release/*.nupkg

# Packages output to:
# src/KurrentDB.Client/bin/Release/KurrentDB.Client.{version}.nupkg
# src/KurrentDB.Client/bin/Release/KurrentDB.Client.{version}.snupkg (symbols)
```

---

## Critical Integration Points

### Protocol Buffers Integration

**Automatic compilation during build** – No manual protoc needed. Configuration is in `KurrentDB.Client.csproj`.

**Protocol files:** `proto/kurrentdb/protocol/v1/`, `proto/kurrentdb/protocol/v2/`, `proto/kurrent/rpc/`

### Source Generator Integration

**Two custom generators:**

1. **KurrentOperationErrorGenerator** - Error exception generation
   - Scans for `[KurrentOperationError]` attributes
   - Generates exception classes with Google RPC Status integration
   - Extracts error metadata (ResourceInfo, RetryInfo, DebugInfo)

2. **Variant.SourceGenerators** - Discriminated union generation
   - Generates code for `IVariant<T0, T1, ...>` implementations
   - Creates Switch/Match methods for exhaustive handling
   - Implicit operators for ergonomic construction

**Configuration:**
```xml
<ProjectReference
    Include="..\Kurrent.Client.SourceGenerators\Kurrent.Client.SourceGenerators.csproj"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false"
    PrivateAssets="all"
/>
```

### gRPC Communication Pattern

**Connection string formats:**
```bash
# Single-node
kurrentdb://localhost:2113

# Cluster discovery (gossip-based)
kurrentdb+discover://node1:2113,node2:2113,node3:2113

# With TLS
kurrents://localhost:2113?tls=true

# With credentials
kurrentdb://admin:changeit@localhost:2113
```

**Node Preference:**
- `Leader` - Route writes to leader (consistency)
- `Follower` - Route reads to followers (load distribution)
- `Random` - Random node selection
- `ReadOnlyReplica` - Read-only replicas only

### Schema Registry Integration

**Type-safe serialization flow:**
```
CLR Object → MessageTypeMapper → SchemaManager → Serializer → Protocol Buffer
    ↓               ↓                   ↓              ↓
Event Type      Schema Version    Data Format    Serialized Bytes
```

**Serializer configuration:**
```csharp
// In KurrentClient constructor
SerializerProvider = new SchemaSerializerProvider([
    new BytesPassthroughSerializer(),  // No schema
    new JsonSchemaSerializer(
        new() { SchemaRegistration = SchemaRegistrationOptions.AutoMap },
        schemaManager),
    new ProtobufSchemaSerializer(
        new() { SchemaRegistration = SchemaRegistrationOptions.AutoMap },
        schemaManager)
]);
```

**Auto-registration workflow:**
1. CLR type → MessageTypeMapper resolves event type name
2. SchemaManager checks registry for schema
3. If not found and AutoMap enabled, generate and register schema
4. Serialize data according to schema format (JSON/Protobuf/Avro)

---

## AI Agent Guidelines

### When Creating New Operations

1. **Follow operation-based organization:**
    ```
    Modules/{Module}/Operations/{Operation}/
    ├── {Module}Client.{Operation}.cs              # Core logic
    ├── {Module}Client.{Operation}.Models.cs       # DTOs
    ├── {Module}Client.{Operation}.Mappers.cs      # Protocol mapping
    ├── {Module}Client.{Operation}.Extensions.cs   # Extensions
    └── {Module}Client.{Operation}.Models.Builders.cs  # Builders
    ```

2. **Return Result types:**
    ```csharp
    // ALL public operations return Result<TSuccess, TError>
    public async ValueTask<Result<OperationSuccess, OperationError>>
        OperationName(params, CancellationToken ct)
    ```

3. **Define errors with source generation:**
    ```csharp
    // Create error variant
    public partial class OperationError : IVariantException<
        FirstException,
        SecondException,
        KurrentException> { }
    
    // Define specific errors
    [KurrentOperationError("SPECIFIC_ERROR_CODE")]
    public partial class FirstException;
    ```

4. **Use ConfigureAwait(false) everywhere in library code**

### When Adding Modules

1. **Inherit from ClientModuleBase:**
    ```csharp
    public partial class NewModuleClient : ClientModuleBase {
        public NewModuleClient(KurrentClient client)
            : base(client, nameof(NewModuleClient)) { }
    
        // Logger, SerializerProvider, ServerCapabilities available from base
    }
    ```

2. **Register in KurrentClient:**
    ```csharp
    // In KurrentClient constructor
    NewModule = new NewModuleClient(this);
    
    // Public property
    public NewModuleClient NewModule { get; }
    ```

3. **Organize by operations, not technical layers**

### When Writing Tests

1. **Use snake_case for test method names (NON-NEGOTIABLE)**
2. **Inherit from appropriate fixture:**
   - `TestFixture` - Unit tests with DI
   - `KurrentClientTestFixture` - Integration tests with client
3. **Use custom Result assertions:**
    ```csharp
    await operation.ShouldNotThrowOrFailAsync(value => { ... });
    await operation.ShouldFailAsync(error => { ... });
    ```
4. **Add timeout attribute:**
    ```csharp
    [Test, Timeout(TestTimeoutMs)]
    public async Task test_method_name(CancellationToken ct) { }
    ```

### When Handling Errors

1. **Never throw exceptions for expected failures**
2. **Always return Result types from public APIs**
3. **Use pattern matching for error handling:**
    ```csharp
    return result.Match(
        success => ProcessSuccess(success),
        error => error switch {
            SpecificError e => HandleSpecific(e),
            _ => throw error.AsException()  // Last resort only
        }
    );
    ```
4. **Provide structured error details:**
    ```csharp
    new KurrentErrorDetails {
        Reason = ErrorReason.From("OPERATION_FAILED"),
        Metadata = Metadata.New
            .With("context1", value1)
            .With("context2", value2),
        ResourceInfo = ResourceInfo.From("Stream", streamName),
        RetryInfo = RetryInfo.From(TimeSpan.FromSeconds(1))
    }
    ```

### Code Generation Patterns

**When to use source generators:**
- Error type generation (`[KurrentOperationError]`)
- Discriminated unions (`IVariant<T0, T1, ...>`)
- Result types with implicit operators (`IResultBase<TValue, TError>`)

**When NOT to use reflection:**
- This codebase is AOT-compatible
- All type mapping must be registration-based, not reflection-based
- Source generators handle boilerplate at compile-time

### Common Mistakes (DON'T)

1. **DON'T use `dotnet test`** - TUnit requires `dotnet run`
2. **DON'T use Allman brace style** - K&R is mandatory
3. **DON'T use PascalCase for test methods** – Use snake_case
4. **DON'T throw exceptions for expected failures** – Return Result types
5. **DON'T forget `ConfigureAwait(false)`** - Required in all library async code
6. **DON'T use reflection for type mapping** – Use MessageTypeMapper registration
7. **DON'T create folder-based namespaces** – Use feature-based (e.g., `Kurrent.Client.Streams`, not `Kurrent.Client.Modules.Streams`)
8. **DON'T mix operations in one file** – Use partial classes per operation
9. **DON'T use TestContainers** – Use FluentDocker
10. **DON'T use xUnit/NUnit attributes** – Use TUnit attributes (`[Test]`, `[Arguments]`, `[Category]`)

---

## Important Files Reference

**Entry Points:**
- `src/KurrentDB.Client/Kurrent.Client/KurrentClient.cs` - Main client
- `src/KurrentDB.Client/Kurrent.Client/Modules/ClientModuleBase.cs` - Module base

**Core Abstractions:**
- `src/Variant/IVariant.cs` - Discriminated unions
- `src/Variant/Result/Result.cs` - Result monad
- `src/KurrentDB.Client/Kurrent.Client/Errors/KurrentErrorDetails.cs` - Error structure

**Build Configuration:**
- `Directory.Build.props` - Global build settings & Package metadata
- `Directory.Packages.props` - Package versions

**Test Infrastructure:**
- `test/Kurrent.Client.Integration.Tests/Fixtures/KurrentClientTestFixture.cs` - Integration test fixture
- `test/Kurrent.Testing/Containers/KurrentDB/KurrentDBTestContainer.cs` - Docker management
- `test/Kurrent.Testing/appsettings.json` - Test configuration
- `test/Kurrent.Testing/docker-compose.yml` - Observability services

**CI/CD:**
- `.github/workflows/build.yml` - Build and test pipeline
- `.github/workflows/create-release.yml` - Release automation
- `.github/git-commit-instructions.md` - Commit message guidelines
- `/scripts/gencert.sh` - Certificate generation for tests

**Example Operations:**
- `src/KurrentDB.Client/Kurrent.Client/Modules/Streams/Operations/StreamsClient.Append.cs`

---

## Quick Reference

```bash
# Build
dotnet build
dotnet build --framework net10.0

# Test (TUnit - use dotnet run, NOT dotnet test!)
dotnet run --project test/Kurrent.Client.Integration.Tests/Kurrent.Client.Integration.Tests.csproj --framework net10.0

# Test specific category
dotnet run --project test/Kurrent.Client.Integration.Tests/Kurrent.Client.Integration.Tests.csproj --framework net10.0 --treenode-filter "/*/*/*/*[Category=Streams]"

# ONE-TIME: Generate test certificates
sudo ./gencert.sh

# Start observability (Seq + Aspire Dashboard)
cd test/Kurrent.Testing && docker compose up -d

# Stop observability
cd test/Kurrent.Testing && docker compose down

# Clean build
dotnet clean && dotnet build

# Package
dotnet pack --configuration Release
```

**Observability URLs:**
- Seq (logs): http://localhost:5341
- Aspire Dashboard (telemetry): http://localhost:18888
- OTLP endpoint: port 4317

# Code Style Rule: Ban Decorative Separator Comments

Project enforces hard rule against ASCII art section dividers in code comments
