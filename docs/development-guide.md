# Development Guide

**Generated:** 2026-01-16
**Project:** Kurrent.Client

---

## Prerequisites

- **.NET SDK:** 8.0, 9.0, or 10.0 (preview)
- **IDE:** JetBrains Rider (recommended) or Visual Studio 2022
- **Docker:** Required for integration tests
- **Git:** For version control

---

## Quick Start

### 1. Clone and Build

```bash
# Clone repository
git clone https://github.com/kurrent-io/Kurrent.Client.git
cd Kurrent.Client

# Restore and build
dotnet restore
dotnet build
```

### 2. Build Specific Framework

```bash
# Build for specific framework
dotnet build --framework net10.0

# Build main client only
dotnet build src/KurrentDB.Client/KurrentDB.Client.csproj
```

---

## Running Tests

### CRITICAL: TUnit Uses `dotnet run`, NOT `dotnet test`

```bash
# Run all integration tests
dotnet run \
  --project test/Kurrent.Client.Integration.Tests/Kurrent.Client.Integration.Tests.csproj \
  --framework net10.0

# Run specific category
dotnet run \
  --project test/Kurrent.Client.Integration.Tests/Kurrent.Client.Integration.Tests.csproj \
  --framework net10.0 \
  --treenode-filter "/*/*/*/*[Category=Streams]"

# Run unit tests
dotnet run \
  --project test/Kurrent.Client.Tests/Kurrent.Client.Tests.csproj \
  --framework net10.0
```

### Test Categories

- `Streams` - Stream operations
- `Registry` - Schema registry
- `PersistentSubscriptions` - Consumer groups
- `ProjectionManagement` - Projections
- `UserManagement` - Security
- `Admin` - Server operations

### Test Infrastructure Setup

```bash
# ONE-TIME: Generate certificates
sudo ./scripts/gencert.sh

# Start observability services (Seq + Aspire Dashboard)
cd test/Kurrent.Testing
docker compose up -d
```

### Observability Endpoints

| Service | URL | Purpose |
|---------|-----|---------|
| Seq | http://localhost:5341 | Structured logging |
| Aspire Dashboard | http://localhost:18888 | Telemetry |
| OTLP | localhost:4317 | OpenTelemetry endpoint |

---

## Code Style

### NON-NEGOTIABLE Rules

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
       // ...
   }
   ```

2. **Indentation:** 4 spaces for C#, 2 spaces for XML/JSON

3. **ConfigureAwait(false)** - REQUIRED in all library async code
   ```csharp
   var result = await SomeOperation().ConfigureAwait(false);
   ```

4. **Test Method Names:** snake_case
   ```csharp
   [Test]
   public async Task registers_initial_version_of_new_schema(CancellationToken ct) { }
   ```

5. **Nullable Reference Types:** Enabled by default
   ```csharp
   public string? OptionalValue { get; set; }  // Nullable
   public string RequiredValue { get; set; }   // Never null
   ```

---

## Architecture Patterns

### Result Pattern (No Exceptions for Flow Control)

```csharp
// ALL public API methods return Result<TValue, TError>
public async ValueTask<Result<AppendSuccess, AppendError>> AppendAsync(...);

// Consumers use pattern matching
var result = await client.Streams.AppendAsync(...);
return result.Match(
    success => ProcessSuccess(success),
    error => error switch {
        StreamNotFoundException => HandleMissing(),
        _ => throw error.AsException()
    }
);
```

### Discriminated Unions

```csharp
// Define variant types
public readonly partial record struct ReadMessage : IVariant<Record, Heartbeat> { }

// Usage
msg.Switch(
    record => ProcessRecord(record),
    heartbeat => HandleHeartbeat(heartbeat)
);
```

### Module Pattern

```csharp
// All client modules inherit from ClientModuleBase
public partial class NewModuleClient : ClientModuleBase {
    public NewModuleClient(KurrentClient client)
        : base(client, nameof(NewModuleClient)) { }
}
```

---

## Namespace Conventions

```csharp
// Feature-based namespaces (NOT folder-based)
namespace Kurrent.Client.Streams;     // NOT Kurrent.Client.Modules.Streams
namespace Kurrent.Client.Registry;    // NOT Kurrent.Client.Modules.Registry
```

---

## Commit Message Format

Uses Conventional Commits v1.0.0:

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

### Types

- `feat:` - New feature (MINOR version bump)
- `fix:` - Bug fix (PATCH version bump)
- `docs:` - Documentation only
- `refactor:` - Code changes without feature/fix
- `test:` - Adding/modifying tests
- `chore:` - Maintenance tasks

### Breaking Changes

```bash
# Using ! notation
feat!: drop support for .NET 7

# Using footer
feat: new API design

BREAKING CHANGE: Old API removed
```

---

## Package Creation

```bash
# Get current version (MinVer automatic versioning)
dotnet tool restore
dotnet tool run minver -- --tag-prefix=v

# Create packages
dotnet pack --configuration Release

# Inspect package contents
./tools/inspect_nuget.sh src/KurrentDB.Client/bin/Release/*.nupkg
```

---

## CI/CD

### GitHub Actions Workflows

| Workflow | File | Purpose |
|----------|------|---------|
| Build & Test | `.github/workflows/build.yml` | PR validation |
| Create Release | `.github/workflows/create-release.yml` | Release automation |

### Versioning

- **MinVer** - Automatic semantic versioning from Git tags
- **Tag format:** `vMAJOR.MINOR.PATCH` (e.g., `v1.2.3`)
- **Pre-release:** Auto-marked as `alpha` before release

---

## Common Operations

### Adding a New Module

1. Create module class inheriting `ClientModuleBase`
2. Organize by operations (Append, Read, etc.)
3. Register in `KurrentClient` constructor
4. Add public property accessor

### Adding a New Operation

1. Create partial class files:
   - `{Module}Client.{Operation}.cs` - Core logic
   - `{Module}Client.{Operation}.Models.cs` - DTOs
   - `{Module}Client.{Operation}.Mappers.cs` - Protocol mapping

2. Return `Result<TSuccess, TError>` from public methods

3. Define errors with source generation:
   ```csharp
   [KurrentOperationError("ERROR_CODE")]
   public partial class SpecificException;
   ```

---

## Debugging

### Source Generator Debugging

```csharp
// Generators include debugger launch
#if DEBUG
if (!Debugger.IsAttached) Debugger.Launch();
#endif
```

### Logging Configuration

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Verbose",
      "Override": {
        "Grpc.Net.Client.Internal": "Error"
      }
    }
  }
}
```

---

## Common Mistakes to Avoid

1. **DON'T use `dotnet test`** - TUnit requires `dotnet run`
2. **DON'T use Allman brace style** - K&R is mandatory
3. **DON'T use PascalCase for test methods** - Use snake_case
4. **DON'T throw exceptions for expected failures** - Return Result types
5. **DON'T forget `ConfigureAwait(false)`** - Required in library code
6. **DON'T use reflection for type mapping** - Use MessageTypeMapper
7. **DON'T use TestContainers** - Use FluentDocker
8. **DON'T use xUnit/NUnit** - Use TUnit
