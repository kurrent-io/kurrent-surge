# Incrementalist: `-- test` broken on .NET 10 with Microsoft Testing Platform

## Summary

`incrementalist run -- test` fails on .NET 10 SDK because the new `dotnet test` CLI no longer accepts positional
path arguments. The tool appends the project/solution path as a positional argument, but .NET 10 requires
`--project` or `--solution` named flags.

## Environment

- **Incrementalist.Cmd**: 1.2.0
- **.NET SDK**: 10.0.201
- **OS**: macOS 26.3.1 (arm64)
- **Test framework**: TUnit 1.20.0 (uses Microsoft Testing Platform)
- **global.json**: configured with `"test": { "runner": "Microsoft.Testing.Platform" }`

## Reproduction

### 1. Create a minimal project

```bash
mkdir repro && cd repro
git init
dotnet new tool-manifest
dotnet tool install Incrementalist.Cmd

cat > global.json << 'EOF'
{
  "sdk": { "version": "10.0.100", "rollForward": "latestFeature" },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
EOF

dotnet new sln --name Repro
mkdir -p src/lib src/lib.tests

# Minimal class library
cat > src/lib/Lib.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
EOF

# Minimal TUnit test project
cat > src/lib.tests/Lib.Tests.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TUnit" Version="1.20.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../lib/Lib.csproj" />
  </ItemGroup>
</Project>
EOF

cat > src/lib.tests/Tests.cs << 'EOF'
namespace Lib.Tests;

public class SampleTests
{
    [Test]
    public async Task it_works() => await Assert.That(true).IsTrue();
}
EOF

dotnet sln add src/lib/Lib.csproj src/lib.tests/Lib.Tests.csproj
dotnet build

# Create git history
git add -A && git commit -m "initial"
git checkout -b feature
echo "// change" >> src/lib/Class1.cs
git add -A && git commit -m "change lib"
```

### 2. Verify `dotnet test` works with named flags

```bash
# Works
dotnet test --solution Repro.slnx -c Debug
dotnet test --project src/lib.tests/Lib.Tests.csproj -c Debug

# Fails — positional path no longer accepted on .NET 10
dotnet test src/lib.tests/Lib.Tests.csproj -c Debug
# Error: Specifying a project for 'dotnet test' should be via '--project'.

dotnet test Repro.slnx -c Debug
# Error: Specifying a solution for 'dotnet test' should be via '--solution'.
```

### 3. Run Incrementalist

```bash
dotnet incrementalist run -b main -- test -c Debug
```

**Expected**: tests run against affected projects.

**Actual**: fails with exit code 1.

```
Running 'test -c Debug' against solution Repro.slnx
Executing 'dotnet test -c Debug /path/to/Repro.slnx' for /path/to/Repro.slnx
Command 'dotnet test -c Debug /path/to/Repro.slnx' failed for /path/to/Repro.slnx with exit code 1
System.Exception: Command execution failed with exit code 1
```

## Root Cause

In `RunDotNetCommandTask.cs`, the target path is appended as a positional argument:

```csharp
// Line ~130
if (!_dotnetArgs.Any(x => x is "--project" or "-p"))
{
    process.StartInfo.ArgumentList.Add(target.Path);  // ← positional
}
```

The .NET 10 SDK's new `dotnet test` (Microsoft Testing Platform) **no longer accepts positional path arguments**.
It requires explicit named flags:

- `--project <path.csproj>` for project files
- `--solution <path.slnx>` for solution files

This is a breaking change in the .NET 10 SDK CLI. See: https://aka.ms/dotnet-test-mtp-error

## Suggested Fix

When the dotnet verb is `test`, use the appropriate named flag instead of a positional argument. This affects
both `RunCommandAsync` (per-project) and `RunSolutionBuild` (full solution fallback).

```csharp
// In RunCommandAsync, replace the positional append:
if (!_dotnetArgs.Any(x => x is "--project" or "-p" or "--solution"))
{
    // .NET 10's `dotnet test` requires named flags, not positional paths
    if (_dotnetArgs.Length > 0 && _dotnetArgs[0] == "test")
    {
        var isSolution = target.Path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                      || target.Path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
        process.StartInfo.ArgumentList.Add(isSolution ? "--solution" : "--project");
    }
    process.StartInfo.ArgumentList.Add(target.Path);
}
```

This handles all three cases:
- **Per-project incremental**: `dotnet test --project /path/to/Project.csproj`
- **Full solution fallback**: `dotnet test --solution /path/to/Solution.slnx`
- **Non-test verbs** (`build`, `pack`): unchanged — still uses positional (which still works)

## Impact

- **`-- test`** is broken on .NET 10 SDK with Microsoft Testing Platform
- **`-- build`** and **`-- pack`** are unaffected (still accept positional paths)
- Affects both per-project incremental mode and full-solution fallback mode
- The `run-process` verb is also affected if used with `dotnet test`

## Workaround

None within Incrementalist. Users must use an alternative tool (e.g., `dotnet-affected` which outputs a
project list and lets users control the `dotnet test` invocation themselves).
