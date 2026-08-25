# Schema validation

Two schema agents sit side by side. Each covers a range of JSON Schema drafts the other cannot.

| Agent | Library | Drafts | Native AOT |
|-------|---------|--------|------------|
| `NJsonSchemaAgent` | NJsonSchema 11.6.1 | draft-4 and up | **No.** Uses `dynamic`; the published native binary throws on first use |
| `JsonSchemaNetAgent` | JsonSchema.Net **8.0.5** | draft-6 and up | **Yes.** `aot-report` reports `0 \| 0 \| 0` |

Neither one replaces the other. JsonSchema.Net rejects draft-4 outright, and NJsonSchema throws when
it validates a payload against a 2020-12 document that resolves a `$ref` into `$defs`.

Both expose the same contract — `ISchemaAgent`, `MessageSchema`, `SchemaValidationResult`,
`SchemaCompatibilityResult`. No type from either library reaches that surface.

## Why JsonSchema.Net is pinned to 8.0.5

**8.0.5 is the last release under the MIT licence.** From 9.0.0 the package ships under an Open
Source Maintenance Fee EULA that bills organisations which generate revenue.

Do not raise this version without reading the rest of this file.

### The licence boundary

Every version below is verified from the published `.nuspec`. The whole family crossed over at once.

| Package | Last MIT | First fee-bearing |
|---------|----------|-------------------|
| `JsonSchema.Net` | **8.0.5** | 9.0.0 |
| `JsonPointer.Net` | **6.0.1** | 7.0.0 |
| `Json.More.Net` | **2.2.0** | 3.0.0 |

The pin resolves to exactly that set, and `Humanizer.Core` was already pinned at a version which
satisfies it:

```
JsonSchema.Net/8.0.5      MIT
JsonPointer.Net/6.0.1     MIT
Json.More.Net/2.2.0       MIT
Humanizer.Core/3.0.10     MIT
```

Raising any one of the three pulls a fee-bearing package into the graph, including transitively.
Check `project.assets.json` after any change to these versions.

### What the fee says

The EULA arrived in commit `52058fe8` on 2026-01-15 and took effect on 2026-02-01. It is attached to
the NuGet package itself:

```xml
<!-- json-everything/src/JsonSchema/JsonSchema.csproj -->
<PackageLicenseFile>OSMFEULA.txt</PackageLicenseFile>
<PackageRequireLicenseAcceptance>true</PackageRequireLicenseAcceptance>
```

The fee applies to organisations that use the software in revenue-generating activities and have an
annual gross revenue of US$10,000 or more. The upstream README states it more broadly than the EULA
does — "required to be paid by all organizations or users of any library from this project who
generate revenue" — while the EULA text scopes itself to the "Binary Release" only. Those two
statements do not agree. Treat the package as fee-bearing.

Release 9.0.0 carried **no code change**. Its entire release note reads "Enacted Open Source
Maintenance Fee." Functionally, 8.0.5 is that release.

## What the pin costs

Releases 9.1.0 through 9.4.0 fixed real defects. We do not have them. The three that could plausibly
affect us were measured through `JsonSchemaNetAgent`; the rest are reported upstream only.

| Fixed in | Defect | Status here |
|----------|--------|-------------|
| 9.1.1 | Concurrency fault in `SchemaRegistry` | **REPRODUCES.** See below |
| 9.2.2 | `$id` after `$defs` causes registration collisions | **Measured, does not reproduce.** Neither exporter emits `$id` |
| 9.2.2 | `StackOverflowException` when a self-referential schema nests under `not` | **Measured, does not reproduce.** Evaluates cleanly |
| 9.2.1 | Memory leak in `JsonSchema.FromText()` | Not measured. `JsonSchemaNetAgent` caches by schema hash, so the call runs once per distinct schema |
| 9.1.2 | Reusing a `JsonNode` that already has a parent | Not measured. Presents mainly through schema generation, which we do not use |
| 9.1.4 | `type` keyword mishandles bigint values | Not measured |
| 9.3.0 | Several validation keywords report no error message | Cosmetic. Degrades a message, never a verdict |
| 9.4.0 | Refined URI and IRI validation to consider syntax only | Cosmetic |

### The concurrency fault, measured

16 threads x 400 iterations against the shared `JsonSchemaNetAgent.Instance`, 12 800 validations per
run. Half the iterations reuse a small set of schemas, half introduce schemas no thread has seen.

| Parse path | Runs | Failures per run |
|------------|------|------------------|
| As written | 5 | 586, 17, 33, 29, 31 |
| Serialised by a lock | 4 | 0, 0, 0, 0 |

It fails three ways, all of them loud:

```
InvalidOperationException   non-concurrent collection ... corrupted its state
JsonSchemaException         Overwriting registered schemas is not permitted
SchemaValidationException   fallout from the corrupted registry
```

No run produced a false accept. The fault throws; it never passes data that should have been
refused. Treat it as an availability fault, not a correctness one.

### What we did about it

Two separate causes contribute, and each needed its own answer. Both live in
`JsonSchemaNet/JsonSchemaNetAgent.cs`.

**1. The registry is not thread safe.** `JsonSchema.FromText` writes the parsed document into the
library's process-wide schema registry. Two threads building different schemas at the same time
corrupt its backing dictionary.

A static `Lock` guards the build:

```csharp
static readonly Lock SchemaRegistryGate = new();

static JsonSchema BuildSchema(string definition) {
    lock (SchemaRegistryGate)
        return JsonSchema.FromText(definition);
}
```

The gate is static because the registry it protects is static. It covers the build only — a cache
hit never reaches it, so once a schema is cached its validations stay fully parallel.

**2. The cache could build the same schema twice.** `ConcurrentDictionary.GetOrAdd` does not promise
to invoke its factory once per key. Two threads that miss the same entry each build the schema, and
the second attempt to register an `$id` already in the registry fails with "Overwriting registered
schemas is not permitted". The gate prevents corruption but cannot prevent a duplicate build.

Both caches therefore hold `Lazy<MessageSchema>` rather than the schema:

```csharp
ConcurrentDictionary<uint, Lazy<MessageSchema>> ParsedSchemaCache { get; } = new();
```

built with `LazyThreadSafetyMode.ExecutionAndPublication`, which runs the factory exactly once per
distinct schema no matter how many threads race for it.

**Result.** The same probe, unchanged, over five consecutive runs:

| | Runs | Failures | Wall clock |
|--|------|----------|------------|
| Before | 5 | 586, 17, 33, 29, 31 | 232-426 ms |
| Gate only | 2 | 28, 16 — all "Overwriting registered schemas" | 342-426 ms |
| Gate and `Lazy` | 5 | **0, 0, 0, 0, 0** | 244-256 ms |

64 000 validations with no failure. The fixed path is also steadier than the original, because the
original spent time throwing.

Remove both when this package moves to JsonSchema.Net 9.1.1 or later.

### The tests that hold this

| Test | Guards |
|------|--------|
| `ConcurrentAccessTests.many_threads_parsing_and_validating_never_fail` | both mitigations. 16 threads x 200 iterations against the shared agent |
| `ConcurrentAccessTests.many_threads_exporting_the_same_type_agree_on_one_definition` | the export cache builds once per type |
| `SchemaReferenceTests.two_schemas_naming_the_same_definition_keep_their_own_rules` | no `$id`-after-`$defs` collision |
| `SchemaReferenceTests.a_self_referencing_definition_under_not_reaches_a_verdict` | no unbounded walk |

The first test was proved to fail, not merely to pass. Reverting `LazyThreadSafetyMode` alone — the
subtler of the two regressions — turns it red with 25 failures, all
`JsonSchemaException: Overwriting registered schemas is not permitted`. Run these against any
version bump of the three packages.

### One gap we closed ourselves

9.4.0 added `EvaluationOptions.IncludeApplicatorErrors`, which suppresses the roll-up an applicator
keyword adds on top of the constraint that actually failed:

```
properties  ->  "Some properties did not match the required schema: ["id","total"]"   <- roll-up
/id         ->  "Value is \"integer\" but should be \"string\""                       <- the real one
```

`JsonSchemaNetMappings.Collect` reproduces that behaviour by dropping any node whose descendant
already reported. Delete it if this package ever moves to 9.4.0 or later, and set the option instead.

## Do not trim the library

`JsonSchema.Net` is already validation-only. Generation, data generation, OpenAPI, array extensions
and the API surface all live in separate packages that this project does not reference.

Its source divides into 76 keyword handlers, 11 localization files and 26 core files. The keyword
handlers are the 2020-12 specification. Removing one does not make the validator smaller — it makes
it wrong, because JSON Schema requires an unrecognised keyword to be **ignored**. A trimmed validator
meets a keyword it never implemented, skips it, and returns valid. In a schema registry that is a
silent accept of data that should have been refused.

## Alternatives, and why each was rejected

| Option | Licence | Outcome |
|--------|---------|---------|
| `NJsonSchema` alone | MIT | Rejected. Throws on 2020-12 `$defs`, and is not AOT-safe |
| `Corvus.JsonSchema` | Apache-2.0 | Rejected. Its runtime-schema path invokes Roslyn at run time to compile each schema. That is dynamic code generation, which Native AOT forbids |
| `Newtonsoft.Json.Schema` | Commercial | Rejected on licence |
| `LateApexEarlySpeed.Json.Schema` | BSD-3-Clause | Not evaluated. Targets netstandard2.1 only and carries no AOT annotation, so its AOT behaviour has to be proved by publishing a native binary and running it — NJsonSchema raised zero analyzer warnings and still crashed |
| A native validator behind P/Invoke | varies | Rejected on cost. It needs a Rust or C toolchain in CI, a per-RID build matrix, both a `cdylib` and a `staticlib`, and custom MSBuild targets — a permanent cross-language pipeline bought to avoid a package fee |
| Vendor the MIT sources of the latest release | MIT | **Still open.** See below |

## If the pin has to move

The source stays MIT. Only the published binary carries the EULA, and the repository `LICENSE` at
HEAD is still the MIT licence. That leaves one route to the newer fixes: build the sources yourself.

- Copy from the **git repository at a commit**, never by unpacking the `.nupkg`. Taking the package
  is what constitutes acceptance of the EULA.
- The schema project has **no release tags** after 7.x, so a pin is a commit SHA.
- `JsonSchema` needs `JsonPointer`, which needs `Json.More`. All three, roughly 155 files.
- Their `.csproj` cannot be reused. It signs with their key, targets four frameworks, builds ten
  localization packages, and sets `PackageLicenseFile` to the EULA. Write a thin project file.
- Four things must carry over or it compiles and then fails at run time: the embedded
  `Meta-Schemas/**/*.json` (minus `OpenAPI/`, `array-ext.json`, `data-2023.json`), the embedded
  `Localization/Resources.resx`, and the `Compile Remove` entries for `PropertyDependenciesKeyword.cs`
  and `PropertyDependency.cs`.
- MIT redistribution requires the copyright and permission notice to ship with it.

**Get a legal opinion before taking this route.** The EULA scopes itself to the binary and states
that self-compiled binaries fall outside it, but the upstream README asserts the fee over any library
from the project. That conflict is unresolved, and it is not an engineering question.

## Scope today

This project is not packable and nothing under `src/` references it. Three test projects consume it.
Nothing here reaches a published package or a customer's dependency graph. That materially narrows
the licence question while it stays true, and it stops being true the moment a production path takes
a dependency on this project.
