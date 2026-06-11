# Contributor Guidelines

- **Repository:** KurrentDB .NET Client
- **Primary Language:** C# 14 (.NET 8.0/9.0/10.0)
- **Last Updated:** 2025-11-07

## Getting Started

### Prerequisites

- .NET SDK 8.0, 9.0, and 10.0 (multi-targeting)
- Docker and Docker Compose (for integration tests)

### Setup

```bash
# Clone repository
git clone <repository-url>
cd Kurrent.Client

# Restore dependencies
dotnet restore

# Build all frameworks
dotnet build

# ONE-TIME: Generate test certificates
sudo ./gencert.sh

# Start observability services (Seq, Aspire Dashboard)
cd test/Kurrent.Client.Testing
docker compose up -d
```

## Development Workflow

### Branch Naming

Pattern: Informal, descriptive style

Recent example:
- `It has begun!` - Initial repository setup

**Recommendation:** Consider adopting [Conventional Commits](https://www.conventionalcommits.org/)
for better changelog generation:
```
feat: add schema registry auto-registration
fix: resolve checkpoint tracking race condition
docs: update testing workflow instructions
```

### Testing Requirements

**CRITICAL:** This project uses **TUnit**, NOT xUnit/NUnit. Use `dotnet run` for test execution.

```bash
# Run all integration tests
dotnet run \
  --project test/Kurrent.Client.Integration.Tests/Kurrent.Client.Integration.Tests.csproj \
  --framework net9.0

# Run specific test category
dotnet run \
  --project test/Kurrent.Client.Integration.Tests/Kurrent.Client.Integration.Tests.csproj \
  --framework net9.0 \
  --treenode-filter "/*/*/*/*[Category=Streams]"

# Run multiple categories
dotnet run \
  --project test/Kurrent.Client.Integration.Tests/Kurrent.Client.Integration.Tests.csproj \
  --framework net9.0 \
  --treenode-filter "/*/*/*/*[Category=Streams]|/*/*/*/*[Category=Registry]"
```

**Available Categories:** Streams, PersistentSubscriptions, ProjectionManagement, UserManagement, Registry, Admin

### Pull Request Process

1. **Create feature branch** from `main`
2. **Implement changes** following code standards (see below)
3. **Add tests** proving fix effectiveness or feature functionality
4. **Run full test suite** for affected categories
5. **Submit PR** using template:
   - [ ] Bugfix / New feature / Breaking change
   - [ ] Tests added
   - [ ] Documentation updated (if appropriate)
6. **Address review feedback** (if applicable)
7. **Merge** to `main` after approval

## Code Standards

### Style Conventions

**K&R Brace Style** - Opening braces on same line:
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

**Indentation:**
- C# code: 4 spaces (tabs rendered as 4 spaces)
- Markup (XML, JSON, YAML): 2 spaces

**Naming:**
- Classes, methods, properties: `PascalCase`
- Private fields: `_camelCase` (underscore prefix required)
- Parameters, locals: `camelCase`
- Test methods: `snake_case` (e.g., `registers_initial_version_of_new_schema`)

**Modern C# Requirements:**
- `var` everywhere (enforced by EditorConfig)
- Expression-bodied members when possible
- Pattern matching over `is`/`as` checks
- Null-coalescing operators (`?.`, `??`)
- File-scoped namespaces: `namespace Kurrent.Client.Streams;`

**Library-Specific:**
- `ConfigureAwait(false)` on ALL awaits in library code (NOT test code)
- Non-nullable references enabled (`<Nullable>enable</Nullable>`)
- Result pattern for public APIs (return `Result<TSuccess, TError>`, NOT exceptions)

Full standards enforced via `.editorconfig` and `Kurrent.Client.sln.DotSettings`.

### Documentation Requirements

**XML Documentation Required For:**
- All public types and members
- Complex internal abstractions
- Non-obvious behavior or constraints

**Update When Applicable:**
- `.project/project.md` - Architecture changes
- `CLAUDE.md` - AI agent instructions (if patterns change)
- Test fixtures - When adding new test infrastructure

**DO NOT Create:**
- Redundant README files (use `.project/project.md`)
- Aspirational documentation (document what exists, not what's planned)

## Communication

### Issue Reporting

Pattern: Structured issue description with reproduction steps

**Template:**
```markdown
**Description:** Brief summary of issue

**Reproduction:**
1. Configure client with X
2. Call operation Y
3. Observe error Z

**Expected:** What should happen
**Actual:** What actually happened

**Environment:**
- Client version: X.Y.Z
- .NET version: net9.0
- OS: Ubuntu 24.04 / macOS 15 / Windows 11
```

### Code Review

**Focus Areas:**
1. Code style compliance (K&R braces, naming, indentation)
2. `ConfigureAwait(false)` presence in library code
3. Result pattern usage (no exception-based flow control)
4. Test coverage for new operations
5. Test method naming (snake_case enforcement)

**Review Checklist:**
- [ ] Builds successfully on all frameworks (net8.0, net9.0, net10.0)
- [ ] Tests pass for affected categories
- [ ] EditorConfig rules satisfied (formatting)
- [ ] XML documentation added for public APIs
- [ ] No breaking changes (or marked as breaking in PR)

## Additional Protocols

### Protocol Buffers

**Automatic compilation** during build - no manual `protoc` invocation needed. Proto files located in:
- `proto/kurrentdb/protocol/v1/` - EventStoreDB compatibility layer
- `proto/kurrentdb/protocol/v2/` - KurrentDB native protocol
- `proto/kurrent/rpc/` - Public RPC API

### Source Generators

Two custom generators active:
1. **KurrentOperationErrorGenerator** - Generates exception classes from `[KurrentOperationError]` attributes
2. **Variant.SourceGenerators** - Generates discriminated union implementations

**Debug generators:** Set configuration to `Debug`, build project - debugger will launch automatically.

### Observability

**Structured logging:** Seq dashboard at <http://localhost:5341>
**Telemetry:** Aspire Dashboard at <http://localhost:18888>
**OTLP endpoint:** Port 4317

Configure verbosity in `test/Kurrent.Client.Testing/appsettings.json`:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Verbose"
    }
  }
}
```

### Versioning

**MinVer** automatic versioning based on git tags:
```bash
# Check current version
dotnet tool restore
dotnet tool run minver -- --tag-prefix=v
```

### Package Creation

```bash
# Create NuGet packages
dotnet pack --configuration Release

# Inspect package contents
./tools/inspect_nuget.sh src/KurrentDB.Client/bin/Release/*.nupkg
```

---

## Quick Reference Commands

```bash
# Build
dotnet build
dotnet build --framework net9.0

# Test (TUnit - use dotnet run!)
dotnet run --project test/Kurrent.Client.Integration.Tests/Kurrent.Client.Integration.Tests.csproj --framework net9.0

# Clean
dotnet clean && dotnet build

# Start observability
cd test/Kurrent.Client.Testing && docker compose up -d

# Stop observability
cd test/Kurrent.Client.Testing && docker compose down
```

---
