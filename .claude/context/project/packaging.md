# Packaging

> How project references become NuGet package contents. Read before adding dependencies to packable projects or creating new ones.

## TL;DR

The root `Directory.Build.targets` provides shorthand flags that expand to the raw metadata and drive
the pack pipeline — prefer these over hand-writing the raw attribute combinations:

| You want to...                              | Reference metadata (shorthand)            | Ends up in             |
|---------------------------------------------|-------------------------------------------|------------------------|
| Declare a NuGet dependency                  | *(none)*                                  | `.nuspec` dependency   |
| Flow ALL assets (incl. analyzers) with it   | `PrivateAssets="none"`                    | `.nuspec` dependency with `include="All"` |
| Embed an internal library                   | `ProjectReference Bundle="true"`          | `lib/{tfm}/`           |
| Vendor a NuGet package's binaries           | `PackageReference Vendor="true"`          | `lib/{tfm}/` + `runtimes/` |
| Use an SDK-internal source generator        | `ProjectReference Analyzer="true"`        | *(nothing — build-time only)* |
| Ship a generator to consumers               | `ProjectReference PackedAnalyzer="true"`  | `analyzers/dotnet/cs/` |

**Footgun:** referencing an unpublished project *without* `PrivateAssets="all"` (or `Bundle="true"`)
produces a `.nuspec` with a NuGet dependency that doesn't exist on any feed.

**Analyzer flow:** NuGet excludes `Build,Analyzers` on dependency edges by default. A consumer that
installs only `Kurrent.Streaming` would NOT get the catalog generator shipped inside `Kurrent.Client` —
which is why Streaming's reference to Client carries `PrivateAssets="none"` (→ `include="All"` in the
nuspec dependency).

Non-packable projects (`IsPackable=false`) just work — `src/NonPackable.targets` handles `dotnet pack --no-build`
automatically for all projects that inherit from the root `Directory.Build.props`. See
[NonPackable.targets Workaround](#nonpackabletargets-workaround) for details.

## Contents

- [Overview](#overview)
- [Reference Categories](#reference-categories)
  - [Public dependency](#public-dependency-becomes-a-nuget-dependency)
  - [Embedded library](#embedded-library-bundled-in-libtfm)
  - [Source generator](#source-generator-bundled-in-analyzersdotnetcs)
- [Package Anatomy](#package-anatomy)
- [How To](#how-to)
  - [Create a new packable project](#create-a-new-packable-project)
  - [Add an internal library](#add-an-internal-library-to-a-packable-project)
  - [Add a source generator](#add-a-source-generator-to-a-packable-project)
  - [Add a project-to-project public dependency](#add-a-project-to-project-public-dependency)
- [Project Templates](#project-templates)
  - [Packable project](#packable-project)
  - [Internal library](#internal-library-embedded-not-published)
  - [Source generator](#source-generator-1)
  - [Roslyn analyzer library](#roslyn-analyzer-library-shared-by-generators)
- [NonPackable.targets Workaround](#nonpackabletargets-workaround)

## Overview

All projects default to `IsPackable=false` (set in the root `Directory.Build.props`). A project becomes a
packable project by explicitly setting `<IsPackable>true</IsPackable>` and adding the
`PackProjectDependencies` and `PackSourceGenerators` targets (see [Packable project](#packable-project)
template). Currently two projects are packable: `Kurrent.Client` and `Kurrent.Streaming`.

A project reference from a packable project falls into one of three categories depending on the metadata on
the `<ProjectReference>` item.

## Reference Categories

### Public dependency (becomes a NuGet dependency)

```xml
<ProjectReference Include="..\Kurrent.Client\Kurrent.Client.csproj" />
```

No special metadata. The SDK declares it as a package dependency in the `.nuspec`. Consumers must install
it separately. Used only when the referenced project is itself a published NuGet package — referencing an
unpublished project this way produces a broken `.nuspec`.

Example: `Kurrent.Streaming` references `Kurrent.Client` this way.

NuGet `<PackageReference>` items (external packages like `Google.Protobuf`) are handled conventionally by the
SDK — no special steps needed.

### Embedded library (bundled in `lib/{tfm}/`)

```xml
<ProjectReference Include="..\Variant\Variant.csproj" PrivateAssets="all" />
```

`PrivateAssets="all"` prevents the reference from becoming a NuGet dependency. The `PackProjectDependencies`
target in each packable project embeds the output DLL into `lib/{tfm}/` alongside the main assembly.

Use this for internal libraries that are not published as their own packages. The DLL ships inside the
packable project's package — no separate install needed.

Each packable project needs a `PackProjectDependencies` target to embed these DLLs. The two existing
packable projects implement it differently:

- **Kurrent.Client** filters `ReferenceCopyLocalPaths` by empty `NuGetPackageId` metadata (which MSBuild sets
  for project-reference assemblies vs NuGet-sourced ones). This automatically picks up all project-reference
  DLLs except the main assembly.
- **Kurrent.Streaming** explicitly calls `GetTargetPath` on references matching
  `PrivateAssets="all" AND ReferenceOutputAssembly != false`, giving precise control.

When creating a new packable project, copy one of these patterns. The [Packable project](#packable-project)
template uses the Client approach; see `Kurrent.Streaming.csproj` for the alternative.

### Source generator (bundled in `analyzers/dotnet/cs/`)

```xml
<ProjectReference Include="..\Variant.SourceGenerators\Variant.SourceGenerators.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false"
                  PrivateAssets="all" />
```

Three metadata attributes work together:

- `OutputItemType="Analyzer"` — tells MSBuild to treat it as a Roslyn analyzer, not a runtime dependency.
- `ReferenceOutputAssembly="false"` — the generator DLL is not copied to the output directory at build time.
  This also excludes it from `ReferenceCopyLocalPaths`, so `PackProjectDependencies` ignores it.
- `PrivateAssets="all"` — prevents it from becoming a NuGet dependency.

The `PackSourceGenerators` target collects these references and places their DLLs in `analyzers/dotnet/cs/`
inside the package. It calls `GetTargetPathWithTargetPlatformMoniker` with `TargetFramework=netstandard2.0`
explicitly, because generators must target netstandard2.0 (Roslyn requirement) regardless of the consuming
project's TFM.

#### Generator transitive dependencies

Source generator projects can have their own project-reference dependencies (e.g. shared analyzer
utilities). These must be bundled alongside the generator DLL in `analyzers/dotnet/cs/` — Roslyn's analyzer
loader needs them at build time.

The generator project handles this with a `GetDependencyTargetPaths` target that hooks into
`GetTargetPathWithTargetPlatformMoniker`. When the packable project's `PackSourceGenerators` queries the
generator for its output paths, the transitive dependency DLLs are returned alongside the generator DLL
automatically. See the [Source generator](#source-generator-1) template for the full implementation.

Both `Kurrent.Client.SourceGenerators` and `Variant.SourceGenerators` use this pattern to bundle
`Kurrent.CodeAnalysis.dll`.

## Package Anatomy

What a packable project's `.nupkg` contains and where each assembly comes from:

```
example.nupkg
├── lib/net10.0/
│   ├── Kurrent.Client.dll                   ← main assembly (SDK default)
│   └── Kurrent.Variant.dll                  ← internal library (PackProjectDependencies)
└── analyzers/dotnet/cs/
    ├── Kurrent.Client.SourceGenerators.dll  ← generator (PackSourceGenerators)
    ├── Kurrent.Variant.SourceGenerators.dll ← generator (PackSourceGenerators)
    └── Kurrent.CodeAnalysis.dll             ← generator dep (GetDependencyTargetPaths)
```

The chain that makes this work:

1. **`PackProjectDependencies`** runs during pack on the packable project. It resolves all project
   references and embeds DLLs from non-NuGet sources (identified by empty `NuGetPackageId` metadata)
   into `lib/{tfm}/`. References with `ReferenceOutputAssembly="false"` (generators) are excluded
   automatically since they never appear in `ReferenceCopyLocalPaths`.

2. **`PackSourceGenerators`** runs during pack on the packable project. It calls
   `GetTargetPathWithTargetPlatformMoniker` on each `OutputItemType="Analyzer"` reference, passing
   `TargetFramework=netstandard2.0` explicitly. The returned paths go into `analyzers/dotnet/cs/`.

3. **`GetDependencyTargetPaths`** runs inside each generator project when queried by step 2. It hooks
   into `GetTargetPathWithTargetPlatformMoniker` via `DependsOn` and adds the generator's own project
   reference DLLs (e.g. `Kurrent.CodeAnalysis.dll`) to the output, so they are returned alongside
   the generator DLL itself. Without this, the generator's transitive dependencies would be missing
   from the package and Roslyn would fail to load the generator at build time.

## How To

### Create a new packable project

1. Set `<IsPackable>true</IsPackable>` in the project.
2. Add `PackageId`, `Title`, `Description`, and `PackageTags` properties.
3. Add a `README.md` to the project directory — the root `Directory.Build.props` includes
   `<None Include="README.md" Pack="true" />` for all packable projects. Pack will warn if missing.
4. Add the `PackProjectDependencies` and `PackSourceGenerators` targets (see [Packable project](#packable-project)
   template).
5. Wire the targets into the pack pipeline via `TargetsForTfmSpecificBuildOutput` and
   `TargetsForTfmSpecificContentInPackage`.
6. The root `Directory.Build.props` already provides shared NuGet metadata (authors, license, icon,
   SourceLink, symbol packages, `EnablePackageValidation`) for any project with `IsPackable=true` — no
   need to duplicate it.

### Add an internal library to a packable project

1. Create the project with `IsPackable=false` (already the default).
2. Set `<DebugType>embedded</DebugType>` so debuggers can step into the library when it is shipped
   inside another package.
3. Add a `<ProjectReference>` with `PrivateAssets="all"` in the packable project.
4. The packable project's `PackProjectDependencies` target embeds it automatically.

### Add a source generator to a packable project

1. Create the generator project targeting `netstandard2.0` with `IsRoslynComponent=true` and
   `IsPackable=false`. See the [Source generator](#source-generator-1) template for all required properties.
2. If the generator has its own project-reference dependencies, add the `GetDependencyTargetPaths`
   target (shown in the template) to bundle them into the package alongside the generator.
3. Add a `<ProjectReference>` with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"
   PrivateAssets="all"` in the packable project.
4. The packable project's `PackSourceGenerators` target embeds it automatically.

### Add a project-to-project public dependency

This applies only when both projects are published NuGet packages (e.g. `Kurrent.Streaming` depending on
`Kurrent.Client`).

1. Reference the project with `<ProjectReference Include="..." />` (no special metadata).
2. NuGet declares it as a dependency in the `.nuspec` automatically.

Do not use this for unpublished projects — that produces a broken `.nuspec`. Use `PrivateAssets="all"`
instead.

## Project Templates

See [Package Anatomy](#package-anatomy) to understand how these templates fit together.

### Packable project

The NuGet deliverable. Requires `IsPackable=true`, package metadata, a `README.md` in the project
directory, and two MSBuild targets to embed non-packable dependencies into the `.nupkg`. The root
`Directory.Build.props` provides shared NuGet metadata (authors, license, icon, SourceLink, symbol
packages, `EnablePackageValidation`) automatically for any packable project.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>Kurrent.Example</PackageId>
    <IsPackable>true</IsPackable>
    <Title>Kurrent .NET Example</Title>
    <Product>Kurrent .NET Example</Product>
    <Description>Package description.</Description>
    <PackageTags>kurrent kurrentdb</PackageTags>
    <!-- Wire pack targets: embed internal DLLs and source generators -->
    <TargetsForTfmSpecificBuildOutput>$(TargetsForTfmSpecificBuildOutput);PackProjectDependencies</TargetsForTfmSpecificBuildOutput>
    <TargetsForTfmSpecificContentInPackage>$(TargetsForTfmSpecificContentInPackage);PackSourceGenerators</TargetsForTfmSpecificContentInPackage>
  </PropertyGroup>

  <ItemGroup>
    <!-- Public dependency: no special metadata, becomes a NuGet dependency in the .nuspec.
         Only use for references to other published NuGet packages. -->
    <ProjectReference Include="..\Kurrent.Client\Kurrent.Client.csproj" />

    <!-- Internal library: PrivateAssets="all" prevents NuGet dependency.
         PackProjectDependencies embeds the DLL into lib/{tfm}/ -->
    <ProjectReference Include="..\Variant\Variant.csproj" PrivateAssets="all" />

    <!-- Source generator: OutputItemType="Analyzer" marks it as a Roslyn component.
         ReferenceOutputAssembly="false" excludes it from runtime output (and from
         PackProjectDependencies). PrivateAssets="all" prevents NuGet dependency.
         PackSourceGenerators embeds it into analyzers/dotnet/cs/ -->
    <ProjectReference Include="..\Variant.SourceGenerators\Variant.SourceGenerators.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" PrivateAssets="all" />
  </ItemGroup>

  <!--
    Embed internal project-reference DLLs into lib/{tfm}/.
    MSBuild sets NuGetPackageId metadata on every resolved assembly: NuGet-sourced assemblies get
    the package id, project-reference assemblies get an empty string. Filter on that to include
    only project-built DLLs. The main assembly is excluded (SDK packs it already).
    Generators never appear here because ReferenceOutputAssembly="false" keeps them out of
    ReferenceCopyLocalPaths.

    Alternative: Kurrent.Streaming uses MSBuild GetTargetPath on PrivateAssets="all" references
    instead of ReferenceCopyLocalPaths filtering. See Kurrent.Streaming.csproj for that pattern.
  -->
  <Target Name="PackProjectDependencies" DependsOnTargets="ResolveReferences">
    <ItemGroup>
      <_ProjectRefDlls Include="@(ReferenceCopyLocalPaths)"
                       Condition="'%(ReferenceCopyLocalPaths.NuGetPackageId)' == '' AND '%(Extension)' == '.dll'" />
      <_ProjectRefDlls Remove="@(_ProjectRefDlls)" Condition="'%(FileName)' == '$(AssemblyName)'" />
      <BuildOutputInPackage Include="@(_ProjectRefDlls)" />
    </ItemGroup>
  </Target>

  <!--
    Embed source generator DLLs into analyzers/dotnet/cs/.
    Calls GetTargetPathWithTargetPlatformMoniker on each Analyzer reference. Generators target
    netstandard2.0 (Roslyn requirement), so TargetFramework is passed explicitly regardless of
    this project's TFM. If a generator has its own ProjectReference dependencies and implements
    GetDependencyTargetPaths, those DLLs are returned alongside the generator DLL automatically.
  -->
  <Target Name="PackSourceGenerators">
    <MSBuild Projects="@(ProjectReference)"
             Condition="'%(ProjectReference.OutputItemType)' == 'Analyzer'"
             Targets="GetTargetPathWithTargetPlatformMoniker"
             Properties="TargetFramework=netstandard2.0">
      <Output TaskParameter="TargetOutputs" ItemName="_SourceGeneratorAssembly" />
    </MSBuild>
    <ItemGroup>
      <TfmSpecificPackageFile Include="@(_SourceGeneratorAssembly)" PackagePath="analyzers/dotnet/cs" />
    </ItemGroup>
  </Target>
</Project>
```

### Internal library (embedded, not published)

Ships inside a packable project's `lib/{tfm}/` via `PackProjectDependencies`. Inherits `net10.0` from
the root `Directory.Build.props`. Use `DebugType=embedded` so debuggers can step into the code when the
DLL is shipped inside another package.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <DebugType>embedded</DebugType>
  </PropertyGroup>
</Project>
```

Referenced from the packable project as:

```xml
<ProjectReference Include="..\MyLibrary\MyLibrary.csproj" PrivateAssets="all" />
```

### Source generator

Ships inside `analyzers/dotnet/cs/` via `PackSourceGenerators`. Must target `netstandard2.0` (Roslyn
requirement) — overrides the `net10.0` default from the root `Directory.Build.props`. The generator is not
a runtime dependency, so `IncludeBuildOutput=false` and `DevelopmentDependency=true`.

If the generator references other projects (e.g. shared analyzer utilities), the
`GetDependencyTargetPaths` target is **required** — it hooks into `GetTargetPathWithTargetPlatformMoniker`
so that when the packable project's `PackSourceGenerators` queries this generator for its output paths,
the transitive dependency DLLs are included in the response. Without it, the dependent DLLs would be
missing from the package and Roslyn would fail to load the generator.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsRoslynComponent>true</IsRoslynComponent>
    <!-- Don't include this DLL in lib/{tfm}/ — it goes in analyzers/dotnet/cs/ via PackSourceGenerators -->
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <!-- Marks the project as a development-only dependency (never flows to consumers) -->
    <DevelopmentDependency>true</DevelopmentDependency>
    <!-- Enforces Roslyn analyzer authoring rules (RS1xxx diagnostics) -->
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>

  <!-- Reference shared analyzer utilities. OutputItemType="Analyzer" tells MSBuild this is a
       Roslyn component. ReferenceOutputAssembly="true" (note: true, not false) so the DLL is
       available at compile time for this generator to use. PrivateAssets="all" keeps it out of
       any NuGet dependency graph. -->
  <ItemGroup>
    <ProjectReference Include="..\Kurrent.CodeAnalysis\Kurrent.CodeAnalysis.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="true"
                      PrivateAssets="all" />
  </ItemGroup>

  <!-- Hook into GetTargetPathWithTargetPlatformMoniker so that when the packable project's
       PackSourceGenerators target queries this generator, all project-reference DLLs are
       returned alongside this generator's own DLL. -->
  <PropertyGroup>
    <GetTargetPathDependsOn>$(GetTargetPathDependsOn);GetDependencyTargetPaths</GetTargetPathDependsOn>
    <GetTargetPathWithTargetPlatformMonikerDependsOn>$(GetTargetPathWithTargetPlatformMonikerDependsOn);GetDependencyTargetPaths</GetTargetPathWithTargetPlatformMonikerDependsOn>
  </PropertyGroup>

  <!-- Collect all project-reference DLLs and add them to the output group.
       IncludeRuntimeDependency="false" prevents these from appearing in the consumer's
       runtime deps — they are only needed by Roslyn's analyzer loader. -->
  <Target Name="GetDependencyTargetPaths" DependsOnTargets="ResolveReferences">
    <ItemGroup>
      <TargetPathWithTargetPlatformMoniker Include="@(ReferenceCopyLocalPaths->WithMetadataValue('Extension', '.dll'))"
                                          IncludeRuntimeDependency="false" />
    </ItemGroup>
  </Target>
</Project>
```

Referenced from the packable project as:

```xml
<ProjectReference Include="..\MyGenerator\MyGenerator.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" PrivateAssets="all" />
```

### Roslyn analyzer library (shared by generators)

Shared code referenced by multiple source generators. Not a generator itself — `IncludeBuildOutput=true`
so its DLL is available for generators to reference, and `GetDependencyTargetPaths` in each generator
bundles it into the package. Must target `netstandard2.0` to match the generators.

Unlike generator projects, `DevelopmentDependency` is not set here — this library is consumed by
generators (not by end users), and the generator's `PrivateAssets="all"` already prevents it from leaking
into any dependency graph.

Roslyn analyzer libraries need `Microsoft.CodeAnalysis.CSharp` to compile against the Roslyn API. The
`Microsoft.CodeAnalysis.Analyzers` package enforces analyzer authoring rules. `PolySharp` provides
polyfills for modern C# features on netstandard2.0.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsRoslynComponent>true</IsRoslynComponent>
    <IncludeBuildOutput>true</IncludeBuildOutput>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers">
      <IncludeAssets>analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="PolySharp">
      <IncludeAssets>analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

Referenced from a generator project as:

```xml
<ProjectReference Include="..\Kurrent.CodeAnalysis\Kurrent.CodeAnalysis.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="true" PrivateAssets="all" />
```

## NonPackable.targets Workaround

`dotnet pack --no-build` propagates `NoBuild=true` as a global property into all projects in the solution,
including non-packable ones reached via P2P references. The SDK's `_CheckForBuildWithNoBuild` guard
fires NETSDK1085 on these projects before `IsPackable` is ever checked. This is a known SDK bug
([dotnet/sdk#3743](https://github.com/dotnet/sdk/issues/3743), unfixed as of .NET 10).

`src/NonPackable.targets` overrides the guard to only fire for packable projects. It is imported via
`AfterMicrosoftNETSdkTargets` in the root `Directory.Build.props` — the only import hook that runs after
the SDK redefines the target. `Directory.Build.targets` cannot be used because it imports before the SDK
target definition and gets overwritten.

This fix is automatic for all projects that inherit from the root `Directory.Build.props` (`src/`, `test/`,
`perf/`). No per-project changes needed.
