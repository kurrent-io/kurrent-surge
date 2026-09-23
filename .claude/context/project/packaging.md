# Packaging

> How project references become NuGet package contents. Read before adding dependencies to packable
> projects or creating new ones. The mechanism lives in three files. The root `Directory.Build.targets`
> holds the pack targets and the suppression. The root `Directory.Build.props` holds
> `@(PackedAnalyzerProject)` and the README, icon, and license items. `src/NonPackable.targets` holds the
> `--no-build` workaround. Packable projects only set `IsPackable=true` and flag their references. There
> are no per-project pack targets.

## TL;DR

Shorthand flags on references drive the whole pack pipeline. Prefer these over hand-writing raw
`PrivateAssets`/`OutputItemType` combinations:

| You want to...                            | Reference metadata (shorthand)           | Ends up in                                |
|-------------------------------------------|------------------------------------------|-------------------------------------------|
| Declare a NuGet dependency                | *(none)*                                 | `.nuspec` dependency                      |
| Flow ALL assets (incl. analyzers) with it | `PrivateAssets="none"`                   | `.nuspec` dependency with `include="All"` |
| Embed an internal library                 | `ProjectReference Bundle="true"`         | `lib/{tfm}/`, plus everything it bundles, packs, or ships as `build/*.props` |
| Vendor a NuGet package's binaries         | `PackageReference Vendor="true"`         | `lib/{tfm}/` + `runtimes/`                |
| Use an SDK-internal source generator      | `ProjectReference Analyzer="true"`       | *(nothing, build-time only)*             |
| Ship a generator to consumers             | `ProjectReference PackedAnalyzer="true"` | `analyzers/dotnet/cs/`                    |

**Footgun:** referencing an unpublished project *without* `Bundle="true"` (or one of the analyzer
flags) produces a `.nuspec` with a NuGet dependency that doesn't exist on any feed → consumers get
`NU1101`.

**Bundles nest.** A bundled project's own `Bundle`, `PackedAnalyzer`, and `build/*.props` items ship in
the packing project's package, at any depth. `Kurrent.Surge` bundles `Kurrent.Surge.Core`. Core bundles
`Variant` and `RecordFilters` and packs two generators. All of it lands in `Kurrent.Surge.nupkg` with one
flag on Surge's reference. Nothing is listed by hand.

The nuspec dependency list does not nest. NuGet reads it from the restore graph. So the packing project
declares every package that a bundled project needs and that nothing else in the packing project's graph
pulls. For Core, Surge declares `NJsonSchema`, `NodaTime`, `NodaTime.Serialization.SystemTextJson`, and
`Ulid`. `verify-package.py` fails when one is missing.

The **in-repo** counterpart is a separate problem: getting that same generator to run on projects that
reference the SDK by source. Analyzers are not transitive through a `ProjectReference`. See
[In-repo source consumers](#in-repo-source-consumers-packedanalyzerproject).

Non-packable projects (`IsPackable=false`) just work. `src/NonPackable.targets` handles `dotnet pack --no-build`
automatically for all projects that inherit from the root `Directory.Build.props`. See
[NonPackable.targets Workaround](#nonpackabletargets-workaround).

**Validate any change with `scripts/verify-package.py`**. Never eyeball a nuspec or hand-unzip a
package. See [Verifying a package](#verifying-a-package).

## Contents

- [Overview](#overview)
- [Reference Categories](#reference-categories)
  - [Public dependency](#public-dependency-becomes-a-nuget-dependency)
  - [Embedded library](#embedded-library-bundled-in-libtfm)
  - [Vendored package](#vendored-package-binaries-in-libtfm--runtimes)
  - [Source generator](#source-generator-build-time-only)
  - [Packed generator](#packed-generator-shipped-in-analyzersdotnetcs)
  - [In-repo source consumers](#in-repo-source-consumers-packedanalyzerproject)
- [How Suppression Works](#how-suppression-works)
- [Package Anatomy](#package-anatomy)
- [How To](#how-to)
- [Verifying a package](#verifying-a-package)
- [Project Templates](#project-templates)
- [NonPackable.targets Workaround](#nonpackabletargets-workaround)

## Overview

All projects default to `IsPackable=false` (root `Directory.Build.props`). A project becomes packable by
setting `<IsPackable>true</IsPackable>` and adding package metadata (`PackageId`, `Title`, `Description`,
`PackageTags`) plus a `README.md`. **That is all the project needs.** The root `Directory.Build.targets`
detects `IsPackable=true` and wires the pack pipeline (`_CollectVendoredAndBundledFiles`,
`_CollectPackedAnalyzers`) automatically. There are no per-project pack targets.

Currently packable: `Kurrent.Surge`. `Kurrent.Surge.Core` is a project bundled inside it.

How a reference is treated is controlled by **metadata flags** on the `<ProjectReference>` /
`<PackageReference>` item. The next section covers each flag; [How Suppression Works](#how-suppression-works)
explains the one subtle part: keeping bundled and vendored types flowing in-repo while hiding them from the
nuspec.

## Reference Categories

### Public dependency (becomes a NuGet dependency)

```xml
<ProjectReference Include="..\Some.Published.Package\Some.Published.Package.csproj" />
```

No special metadata. The SDK declares it as a package dependency in the `.nuspec`. Use only when the
referenced project is itself a published NuGet package. No project in this repo does this today: the
only package is `Kurrent.Surge`, and everything it depends on in-repo is bundled. External
`<PackageReference>` items (`Google.Protobuf`, `kurrent.quack`, …) are handled conventionally by the SDK.

### Embedded library (bundled in `lib/{tfm}/`)

```xml
<ProjectReference Include="..\Variant\Variant.csproj" Bundle="true" />
```

`Bundle="true"` ships the referenced project's output DLL inside `lib/{tfm}/` alongside the main
assembly, and hides it from the nuspec dependency list. `_CollectVendoredAndBundledFiles` calls
`_GetBundledClosure` on the referenced project. That target returns the project's own DLL, resolved
through `GetTargetPath` so that `AssemblyName` is honoured regardless of the project filename. It then
calls itself on every `Bundle="true"` reference the project carries, so the result covers any depth.
Two sibling targets do the same walk: `_GetPackedAnalyzerClosure` for `PackedAnalyzer` references, and
`_GetBundledBuildProps` for `build/*.props` items. The props file is renamed to `build/{PackageId}.props`
on the way in. Two such files across the closure is an error. The three targets live in
`Directory.Build.targets`, so every project answers them.

Use for internal libraries that are not published as their own package (e.g. `Variant`, which ships as
`Kurrent.Variant.dll`). The library project stays `IsPackable=false` and uses
`<DebugType>embedded</DebugType>` so debuggers can step into it when shipped inside another package.

A bundled project's package dependencies do **not** flow into the nuspec (see [TL;DR](#tldr)). Declare
on the packing project any that nothing else in its graph pulls; `verify-package.py` reports the exact
set as missing files under `lib/`.

### Vendored package (binaries in `lib/{tfm}/` + `runtimes/`)

```xml
<PackageReference Include="Some.Native.Package" Vendor="true" />
```

`Vendor="true"` ships a NuGet package's binaries inside our package (managed DLLs into `lib/{tfm}/`,
satellite assemblies into `lib/{tfm}/<culture>/`, native assets into `runtimes/<rid>/native/`) and hides
it from the nuspec dependency list. `_CollectVendoredAndBundledFiles` collects the files from
`ReferenceCopyLocalPaths` matched by `NuGetPackageId`.

No project vendors a package today. **Caveat:** Vendor suppression is restore-graph based (see
[How Suppression Works](#how-suppression-works)) and shares the same pack-time requirement as Bundle;
validate with `verify-package.py` if you introduce one.

### Source generator (build-time only)

```xml
<ProjectReference Include="..\Variant.SourceGenerators\Variant.SourceGenerators.csproj" Analyzer="true" />
```

`Analyzer="true"` wires the project as a Roslyn source generator that runs while building **this** project
only. `Directory.Build.targets` expands it to `OutputItemType="Analyzer" ReferenceOutputAssembly="false"
PrivateAssets="all"`. The generator runs, its DLL is not copied to output, and it never appears in the
package or its nuspec. Use for SDK-internal generators (e.g. `Variant.SourceGenerators`) that
consumers do not need.

### Packed generator (shipped in `analyzers/dotnet/cs/`)

```xml
<ProjectReference Include="@(PackedAnalyzerProject)" PackedAnalyzer="true" />
```

`PackedAnalyzer="true"` does everything `Analyzer="true"` does **and** ships the generator DLL into
`analyzers/dotnet/cs/` so it runs in **consumer** compilations. `_CollectPackedAnalyzers` queries each
flagged project's `GetTargetPathWithTargetPlatformMoniker` with `TargetFramework=netstandard2.0` (Roslyn's
requirement, regardless of the packable project's TFM) and places the result under `analyzers/dotnet/cs/`.

`@(PackedAnalyzerProject)` in `Directory.Build.props` holds the generators that must **also** reach
in-repo source consumers; today that is one project, `Kurrent.Surge.Schema.Catalogs.Generators`.
`Kurrent.Surge.Core` references it with `PackedAnalyzer="true"` and, separately, flags
`Kurrent.Surge.Generators` the same way by direct reference. Both ship under `analyzers/dotnet/cs/` of
`Kurrent.Surge.nupkg`, because Surge bundles Core and the packed-analyzer walk nests; only the ones in the
item are additionally wired for source consumers (below).

> **Generator helper code is baked in by source, not shipped as a DLL.** Generators include shared Roslyn
> helpers via `Kurrent.CodeAnalysis.Sources.props` (source compiled into each generator), so each packed
> generator is a single self-contained DLL. There is no `Kurrent.CodeAnalysis.dll` in the package and no
> `GetDependencyTargetPaths` dance. A generator that fails with "Could not load Kurrent.CodeAnalysis"
> is referencing it as a DLL instead of importing the sources props; validate the fix with
> `--no-incremental`, since an up-to-date incremental build will not re-run the generator.

### In-repo source consumers (`@(PackedAnalyzerProject)`)

A packed generator reaches real NuGet consumers automatically (it ships under `analyzers/dotnet/cs/`). But
**in-repo** projects (examples, tests, and `Kurrent.Surge` itself) reference the SDK by *source* (`ProjectReference`),
and **Roslyn analyzers are NOT transitive through a `ProjectReference`**, by design in the SDK. So a
project that references `Kurrent.Surge`/`Kurrent.Surge.Core` from source does not get the catalog generator
the way a packaged consumer does; without help its `[MessageCatalog]` / `[MessageType]` declarations would
generate nothing.

The fix is centralized:

- **`@(PackedAnalyzerProject)`** (`Directory.Build.props`) is the single source of truth for the generators
  that ship to consumers. `Kurrent.Surge.Core` references it with `PackedAnalyzer="true"`.
- A rule in `Directory.Build.targets` wires that **same list** as analyzers onto any in-repo project that
  references `Kurrent.Surge` or `Kurrent.Surge.Core` by source. The rule collects those references as
  `_SdkSourceRef` items. `Kurrent.Surge` references `Kurrent.Surge.Core` by source, so the rule matches
  it too, and the catalog generator runs on `Kurrent.Surge`. The rule does not apply when
  `TestPackages=true`. In package mode, the package supplies the generator, and a second copy emits the
  types twice (`CS0101`).

Detection filters `@(ProjectReference)` by filename via an item-to-item transform
(`@(ProjectReference->WithMetadataValue('Filename', 'Kurrent.Surge'))`), **not** a property: MSBuild
evaluates properties before items, so a property assigned `@(ProjectReference)` reads as the literal,
unexpanded string.

**To add another packed generator that flows everywhere:** add one line to `@(PackedAnalyzerProject)`. It
then ships in the package AND reaches every in-repo source consumer, with no per-project wiring.

## How Suppression Works

Hiding a bundled/vendored reference from the nuspec dependency list while still letting its types flow to
in-repo `ProjectReference` consumers is the one subtle part of the pipeline.

- **Analyzer / PackedAnalyzer** refs get `PrivateAssets="all"` **unconditionally** (eval-time). They never
  become nuspec dependencies and never flow at runtime. That is exactly what an analyzer should do.
- **Bundle / Vendor** refs get `PrivateAssets="all"` **only in the pack-context restore**
  (`Condition="'$(_SuppressFlaggedDeps)' == 'true'"`). Only `_RestoreForPack` passes
  `_SuppressFlaggedDeps`. The CLI never sets it. During a normal `build`/`run`/`test` the refs carry no
  `PrivateAssets`, so their types flow transitively to consumers like any ordinary reference. At pack,
  `PrivateAssets="all"` excludes them from the nuspec.

The gate is not `_IsPacking`. The SDK sets `_IsPacking` as a global property, so it flows into every
referenced project. A gate on `_IsPacking` makes `dotnet pack Kurrent.Surge` restore `Kurrent.Surge.Core`
with its bundled edges private. That restore writes `PrivateAssets="all"` into the downstream assets file
and removes the bundled types from a compilation that needs them (`CS0234` on `Kurrent.Variant`).

The catch: **NuGet's `PackTask` computes project→project dependencies from the restore graph
(`project.assets.json` → `RestoreMetadata.TargetFrameworks[].ProjectReferences[].PrivateAssets`)**, not from
a pack-time MSBuild item. `PrivateAssets` lands in that file only when the **restore** that produced it ran
under `_SuppressFlaggedDeps`. A pack that reuses a non-pack restore graph, such as `dotnet pack --no-restore` or
`--no-build` (which skips the restore that would rewrite it), as CI and release do, reads
`PrivateAssets="None"` and **leaks the bundled project into the nuspec** (the `NU1101` bug).

The **`_RestoreForPack`** target closes this: when `_IsPacking` is set and the project has any Bundle/Vendor
refs, it forces a pack-context restore (`Restore` with `_SuppressFlaggedDeps=true;RestoreForce=true`)
before `GenerateNuspec`, so the assets file always carries the suppression, **regardless of how pack was
invoked**. Normal `build`/`test`/`run` never run `_RestoreForPack`, so their restore graph keeps
`PrivateAssets="None"` and the refs keep flowing.

> Pack-time MSBuild item edits do NOT work for this: `@(_ProjectReferencesWithVersions)` (the item
> `GenerateNuspec` passes to `PackTask`) only supplies version overrides. Removing an entry from it does
> not drop the dependency, because `PackTask` re-reads the assets file from disk. The restore graph is the
> only lever.

## Package Anatomy

What a packable project's `.nupkg` contains and where each assembly comes from:

```
Kurrent.Surge.nupkg
├── lib/net10.0/
│   ├── Kurrent.Surge.dll                           ← main assembly (SDK default)
│   ├── Kurrent.Surge.Core.dll                      ← Bundle on Surge
│   ├── Kurrent.Variant.dll                         ← Bundle on Core, nested
│   └── Kurrent.Surge.RecordFilters.dll             ← Bundle on Core, nested
├── analyzers/dotnet/cs/
│   ├── Kurrent.Surge.Generators.dll                ← PackedAnalyzer on Core, nested
│   └── Kurrent.Surge.Schema.Catalogs.Generators.dll ← PackedAnalyzer on Core, nested
└── build/
    └── Kurrent.Surge.props                         ← Core's build/Kurrent.Surge.Core.props, renamed to this package's id
```

`Variant.SourceGenerators` is `Analyzer="true"` (SDK-internal) on both `Kurrent.Surge` and
`Kurrent.Surge.Core`. It runs while building them and ships **nothing**. `Kurrent.Surge.Generators` is
`Analyzer="true"` on Surge (runs there) and `PackedAnalyzer="true"` on Core (ships). The nuspec lists only
the real external dependencies (`Google.*`, `Grpc.*`, `Microsoft.Extensions.*`, `NJsonSchema`, `NodaTime`,
`Ulid`, …); `Kurrent.Surge.Core`, `Kurrent.Variant`, and the generators are absent.
`Kurrent.Surge.Schema.Catalogs.Generators` also runs on Surge through the `_SdkSourceRef` rule (see
[In-repo source consumers](#in-repo-source-consumers-packedanalyzerproject)).

The targets that build this, all in the root `Directory.Build.targets`, auto-wired for every
`IsPackable=true` project:

1. **`_CollectVendoredAndBundledFiles`** (`TargetsForTfmSpecificBuildOutput`). Bundle projects →
   `lib/{tfm}/` via `_GetBundledClosure` (recursive); Vendor packages → `lib/{tfm}/` + `runtimes/` via
   `ReferenceCopyLocalPaths`.
2. **`_CollectPackedAnalyzers`** (`TargetsForTfmSpecificContentInPackage`). PackedAnalyzer projects, own
   and nested, → `analyzers/dotnet/cs/` via `_GetPackedAnalyzerClosure`
   (`TargetFramework=netstandard2.0`).
3. **`_CollectBundledBuildProps`** (`TargetsForTfmSpecificContentInPackage`). A bundled project's
   `build/*.props` → `build/{PackageId}.props` via `_GetBundledBuildProps`.
4. **`_RestoreForPack`** (`BeforeTargets="GenerateNuspec"`). Guarantees the restore graph carries the
   Bundle/Vendor suppression regardless of pack invocation (see [How Suppression Works](#how-suppression-works)).

## How To

### Create a new packable project

1. Set `<IsPackable>true</IsPackable>`.
2. Add `PackageId`, `Title`, `Description`, and `PackageTags`.
3. Add a `README.md` in the project directory. The root `Directory.Build.props` includes it via
   `<None Include="README.md" Pack="true" />`; pack warns if it's missing.
4. Flag references as needed (`Bundle`, `Vendor`, `Analyzer`, `PackedAnalyzer`, or `PrivateAssets="none"`).

That's it. The root `Directory.Build.targets` wires the pack targets and `Directory.Build.props` provides
shared NuGet metadata (authors, license, icon, SourceLink, symbol packages, `EnablePackageValidation`). **Do
not** add per-project pack targets.

### Add an internal library to a packable project

1. Create the library with `IsPackable=false` (the default) and `<DebugType>embedded</DebugType>`.
2. Reference it with `Bundle="true"`. Its DLL is embedded into `lib/{tfm}/` automatically.

### Add a source generator to a packable project

1. Create the generator targeting `netstandard2.0` with `IsRoslynComponent=true` and `IsPackable=false`.
   Pull in shared Roslyn helpers via `Kurrent.CodeAnalysis.Sources.props` (source, not a DLL reference).
2. Reference it with `Analyzer="true"` (build-time only). If it must also run in **consumer**
   compilations, add it to `@(PackedAnalyzerProject)` in `Directory.Build.props` and reference that with
   `PackedAnalyzer="true"`. The single list both packs it and wires it for in-repo source consumers.

### Add a project-to-project public dependency

Only when both projects are published packages. Reference with no special metadata; add
`PrivateAssets="none"` if the dependency ships an analyzer that consumers need to receive transitively.
Never use a bare reference for an unpublished project. Use `Bundle="true"`.

## Verifying a package

`scripts/verify-package.py` packs a project (or inspects an existing `.nupkg`) and checks it against the
Bundle/Vendor/Analyzer contract declared in its csproj, including the full transitive binary closure.

```sh
# Pack and verify (default: packs the CI way: standalone restore, build --no-restore, then --no-build --no-restore pack)
python3 scripts/verify-package.py --csproj src/Kurrent.Surge/Kurrent.Surge.csproj --output-dir .artifacts/local-packages

# Verify an existing package
python3 scripts/verify-package.py path/to/Kurrent.Surge.1.2.3.nupkg --csproj src/Kurrent.Surge/Kurrent.Surge.csproj
```

By default it packs **the way CI ships**: a standalone `dotnet restore`, then `dotnet build --no-restore`,
then `dotnet pack --no-build --no-restore`. That is the path
that surfaces restore-graph-dependent leaks. A plain `dotnet pack` (`--fresh-pack`) does its own
pack-owned restore and can hide them, which is why the default is the CI path. It asserts that:

- bundled DLLs are present in `lib/`, including a bundle's own nested bundles
- vendored binaries and native assets are present
- analyzers and direct bundles are absent from the nuspec dependencies
- packed generators are present in `analyzers/dotnet/cs/`, nested ones included
- a bundled `build/*.props` is present under this package's id
- every real dependency is declared
- every package in a bundled project's closure is either reachable from the declared dependencies or
  shipped in `lib/`

The script has two limits:

- If a `PackedAnalyzer` reference names a project path, the script looks for that generator's DLL by
  name. It cannot resolve an `@(PackedAnalyzerProject)` include to one DLL. For those includes, it checks
  only that `analyzers/dotnet/cs/` holds at least one DLL. A missing `Kurrent.Surge.Schema.Catalogs.Generators.dll`
  passes when `Kurrent.Surge.Generators.dll` is present.
- It checks that nested bundles are present in `lib/`. It does not check that they are absent from the
  nuspec dependencies.

## Project Templates

### Packable project

The NuGet deliverable. Requires only `IsPackable=true`, package metadata, a `README.md`, and flagged
references. No custom pack targets.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>Kurrent.Example</PackageId>
    <IsPackable>true</IsPackable>
    <Title>Kurrent .NET Example</Title>
    <Product>Kurrent .NET Example</Product>
    <Description>Package description.</Description>
    <PackageTags>kurrent kurrentdb</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <!-- Public dependency: becomes a NuGet dependency. Only for other published packages. -->
    <ProjectReference Include="..\Some.Published.Package\Some.Published.Package.csproj" />

    <!-- Internal library: shipped in lib/{tfm}/, hidden from nuspec deps. -->
    <ProjectReference Include="..\Variant\Variant.csproj" Bundle="true" />

    <!-- SDK-internal generator: runs at build, ships nothing. -->
    <ProjectReference Include="..\Variant.SourceGenerators\Variant.SourceGenerators.csproj" Analyzer="true" />

    <!-- Generator that must also run in consumer compilations. -->
    <ProjectReference Include="@(PackedAnalyzerProject)" PackedAnalyzer="true" />
  </ItemGroup>
</Project>
```

### Internal library (embedded, not published)

Ships inside a packable project's `lib/{tfm}/` via `Bundle="true"`. Inherits `net10.0` from the root
`Directory.Build.props`. Use `DebugType=embedded` so debuggers can step into it inside another package.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <DebugType>embedded</DebugType>
  </PropertyGroup>
</Project>
```

### Source generator

Ships inside `analyzers/dotnet/cs/` when referenced with `PackedAnalyzer="true"`, or build-time-only with
`Analyzer="true"`. Must target `netstandard2.0` (Roslyn requirement). Shared helpers are included by source
via `Kurrent.CodeAnalysis.Sources.props`, so the generator is a single self-contained DLL.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsRoslynComponent>true</IsRoslynComponent>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <DevelopmentDependency>true</DevelopmentDependency>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>

  <Import Project="..\Kurrent.CodeAnalysis\Kurrent.CodeAnalysis.Sources.props" />
</Project>
```

## NonPackable.targets Workaround

`dotnet pack --no-build` propagates `NoBuild=true` as a global property into all projects in the solution,
including non-packable ones reached via P2P references. The SDK's `_CheckForBuildWithNoBuild` guard fires
NETSDK1085 on these projects before `IsPackable` is ever checked. This is a known SDK bug
([dotnet/sdk#3743](https://github.com/dotnet/sdk/issues/3743), unfixed as of .NET 10).

`src/NonPackable.targets` overrides the guard to only fire for packable projects. It is imported via
`AfterMicrosoftNETSdkTargets` in the root `Directory.Build.props`. That is the only import hook that runs after the
SDK redefines the target. `Directory.Build.targets` cannot be used because it imports before the SDK target
definition and gets overwritten.

This fix is automatic for all projects that inherit from the root `Directory.Build.props` (`src/`, `test/`,
`perf/`, `examples/`). No per-project changes needed.
