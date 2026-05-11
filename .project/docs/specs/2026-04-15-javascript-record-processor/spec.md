# JavaScript Record Processor — Draft Spec
==
> Living document. Captures decisions from design discussions. Not a final spec.

**Status:** Early design discussion
**Date:** 2026-04-15

## Concept

Allow users to implement record processors in JavaScript. The JS script implements the same
lifecycle as `IRecordProcessor` — the SDK handles engine setup, sandboxing, and host object
marshalling.

## Decisions Made

### Package structure — two engine packages

```
Kurrent.Processors.JavaScript.Jint     →  JintRecordProcessor : IRecordProcessor
Kurrent.Processors.JavaScript.Okojo    →  OkojoRecordProcessor : IRecordProcessor
```

Each package is self-contained. No shared abstractions package. Same JS API contract across
both engines — users can swap by changing a NuGet reference and one type name.

**Why both:** Performance matters significantly for record processing. Okojo is ~3x faster
than Jint with ~25,000x fewer allocations. Jint is the stable/proven option. Users choose
based on their priorities.

### Target framework: net10.0 only

SDK is net10.0 only. Both Jint (via netstandard2.0) and Okojo (net10.0 native) work.

### Engine profiles

| | Jint | Okojo |
|---|---|---|
| Speed | Baseline (tree-walk interpreter) | ~3x faster (bytecode) |
| Allocations | High (2.2 MB on loop bench) | Near-zero (88 B on same) |
| NativeAOT | Not AOT-safe | First-class |
| Sandbox | Memory, time, statements, recursion | Instructions, timeout |
| ES support | ES2025 | ES2020+ (87.7% Test262) |
| Maturity | 4.5k stars, battle-tested | New project, single author |

### JS API — mirrors IRecordProcessor exactly

The JS script exports functions matching the C# interface lifecycle:

- `processRecord(context)` — required
- `onCheckpoint(context)` — optional
- `onError(context)` — optional
- `onComplete(context)` — optional

Context objects exposed to JS are exact representations of the C# types
(`ProcessRecordContext`, `CheckpointContext`, `ErrorContext`, `CompleteContext`).
The `Record` object is also an exact JS representation of the C# `Record` type.

### Script source — both inline and file

Users can provide scripts as:
- Inline string
- File path

### Sandboxing — top priority

Security-aware by design. JS code runs in a locked-down sandbox:
- No CLR/host access unless explicitly injected
- No filesystem, network, or process access
- Resource limits (memory, time, instructions) configurable per engine
- Only host-provided capabilities are available

### State management — user's responsibility

No framework-managed state. If users want state across records, they use JS globals
or closures. The SDK does not provide or manage state storage.

### Host capabilities — TBD

The SDK will provide controlled host functions to JS scripts. Confirmed so far:
- **Emit** — append records to streams (exact API TBD)
- **Log** — structured logging via host logger

Open: what else to provide, how extensible, whether users can register custom host
objects/services.

### JS API contract — TypeScript declarations

`kurrent-processor.d.ts` defines the `RecordProcessor` interface and all context/record types
using plain primitives (`string`, `number`). No branded types, no type aliases. Located in
this spec folder.

### Okojo interop — reuse Okojo's collector logic

Okojo's source generator only works on types with `[GenerateJsObject]` in the same assembly.
Our types live in `Kurrent.Client` and `Kurrent.Processors` — different assemblies.

**Solution:** Copy the `JsObjectExportCollector` code from Okojo's source into our project.
Modify the type discovery to accept an explicit list of types instead of searching for
`[GenerateJsObject]` attributes. Same member analysis, same `HostBinding` output — just a
different entry point.

AOT-safe, zero reflection, core packages untouched.

### Jint interop — explicit delegate mapping

Jint is not AOT-safe (uses reflection for object wrapping). For the Jint package, we use
explicit delegate mapping via `SetValue()` — manually wire each property/method. Acceptable
since Jint is the "stable/proven" option, not the performance path.

## Implementation Plan — Okojo Spike

### Step 1: Create the project

```
src/Kurrent.Processors.JavaScript.Okojo/
├── Kurrent.Processors.JavaScript.Okojo.csproj
├── OkojoRecordProcessor.cs          ← implements IRecordProcessor
├── OkojoScriptEngine.cs             ← wraps JsRuntime, loads scripts, manages lifecycle
├── Bindings/
│   ├── RecordBinding.cs             ← HostBinding for Record
│   ├── RecordPositionBinding.cs     ← HostBinding for RecordPosition
│   ├── ContextBindings.cs           ← HostBinding for ProcessRecordContext, etc.
│   └── HostBindingFactory.cs        ← builds HostBinding from type member analysis
└── Settings/
    └── OkojoProcessorSettings.cs    ← sandbox config (maxInstructions, timeout)
```

`.csproj` references:
- `Okojo` (0.1.1-preview.1)
- `Kurrent.Processors` (project reference)

### Step 2: Verify Okojo APIs work

Before writing bindings, spike a minimal test:
1. Create `JsRuntime`, get a `Realm`
2. Install a global function via `JsGlobalInstaller`
3. Execute a script that calls the function
4. Verify the callback fires in .NET

This confirms the manual binding path works. If it doesn't, we know early.

### Step 3: Implement type bindings

For each type we expose to JS, build a `HostBinding` with `HostMemberBinding[]` arrays.
Each property becomes a getter delegate: `(in CallInfo info) => JsValue.FromString(...)`.

Types to bind (in dependency order):
1. `RecordSchemaInfo` — 3 properties (schemaName, schemaFormat, schemaVersionId)
2. `Link` — 4 properties (name, position, revision, stream)
3. `StreamPosition` — 2 properties (stream, revision)
4. `RecordPosition` — 5 properties (logPosition, streamPosition, stream, revision, partition)
5. `Metadata` — key-value access (special handling — index-based)
6. `Record` — 15 properties, all read-only
7. `ThroughputMetrics` — 4 properties
8. `CheckpointProgress` — 4 properties
9. `PartitionProgress` — 2 properties
10. `WorkerProgress` — 4 properties (includes partitions map)
11. `PartitionPositions` — partition-indexed positions
12. `ProcessRecordContext` — record + progress + 3 stop methods
13. `CheckpointContext` — positions + checkpointId + trigger + timestamp
14. `ErrorContext` — progress + error
15. `CompleteContext` — progress

The `IHostBindable` adapter pattern for external types:
```csharp
internal sealed class RecordJsAdapter(Record inner) : IHostBindable {
    public Record Inner => inner;
    public HostBinding GetHostBinding() => RecordBindings.Binding;
}
```

One adapter allocation per record. 24 bytes on x64. Poolable if needed.

### Step 4: Implement OkojoRecordProcessor

```csharp
public class OkojoRecordProcessor : IRecordProcessor {
    readonly JsRuntime _runtime;
    readonly JsRealm _realm;
    readonly JsValue _processRecord;     // cached reference to JS function
    readonly JsValue? _onCheckpoint;
    readonly JsValue? _onError;
    readonly JsValue? _onComplete;

    public OkojoRecordProcessor(OkojoProcessorSettings settings) {
        _runtime = JsRuntime.CreateBuilder()
            .UseGlobals(globals => InstallHostBindings(globals))
            .Build();
        _realm = _runtime.MainRealm;

        // Load script (inline or file)
        _realm.Eval(settings.Script);

        // Cache function references
        _processRecord = _realm.GetGlobal("processRecord");
        _onCheckpoint = TryGetGlobal("onCheckpoint");
        _onError = TryGetGlobal("onError");
        _onComplete = TryGetGlobal("onComplete");
    }

    public async ValueTask ProcessRecord(ProcessRecordContext context) {
        var jsContext = new ProcessRecordContextAdapter(context);
        var result = _realm.Call(_processRecord, jsContext);
        // If result is a Promise, await it
    }
    // ... other lifecycle methods
}
```

### Step 5: Sandbox configuration

```csharp
public record OkojoProcessorSettings {
    public string Script { get; init; }           // inline JS or file path
    public string? ScriptPath { get; init; }      // file path (alternative)
    public int MaxInstructions { get; init; } = 1_000_000;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
    public int CheckInterval { get; init; } = 1000;
}
```

### Step 6: Builder/DI integration (draft API)

```csharp
// Standalone
KurrentProcessors
    .CreateJavaScript(options => {
        options.UseOkojo();
        options.ScriptFile("processor.js");
        options.Sandbox(s => s.MaxInstructions(100_000));
    })
    .ConnectTo("esdb://localhost:2113")
    .WithProcessorId("my-js-processor")
    .RunWithGracefulShutdownAsync();

// DI
services.AddKurrentProcessor<OkojoRecordProcessor>(processor => {
    processor.ScriptFile("processor.js");
    processor.ConnectTo("esdb://localhost:2113");
});
```

### Okojo API reference (from source analysis)

Key APIs we'll use:

```csharp
// Runtime setup
var runtime = JsRuntime.CreateBuilder()
    .UseGlobals(globals => { ... })
    .Build();
var realm = runtime.MainRealm;

// Global installation
realm.InstallGlobals(globals => {
    globals.Function("name", argCount, (in CallInfo info) => { return JsValue.Undefined; });
    globals.Value("name", realm => JsValue.FromInt32(42));
    globals.Property(PropertyDefinition.GetterData(...));
});

// Script execution
realm.Eval("var x = 1 + 2;");

// Sandbox limits
runtime.SetMaxInstructions(count);
runtime.SetExecutionTimeout(timeout);
runtime.SetCheckInterval(interval);

// Host binding (for wrapping .NET objects)
var binding = new HostBinding(typeof(T), instanceMembers: [...], staticMembers: [...]);
var memberBinding = new HostMemberBinding("propName", HostMemberBindingKind.Property,
    isStatic: false,
    getterBody: static (in CallInfo info) => JsValue.FromString(info.GetThis<T>().Prop),
    setterBody: null);  // read-only

// JsValue conversions
JsValue.FromString(str)
JsValue.FromInt32(n)
JsValue.True / JsValue.False
JsValue.Undefined
new JsValue(doubleVal)
info.GetThis<T>()
info.GetArgumentString(index)
info.GetArgumentDouble(index)
info.GetArgument<T>(index)
info.Realm.WrapHostObject(value)  // for IHostBindable objects
info.Realm.WrapHostValue(value)   // general wrapping
info.Realm.WrapTask<T>(task)      // async support
```

## Open Questions

- **Host capabilities model:** What services does the SDK expose to JS? Just emit + log,
  or extensible? Can users register their own .NET services as host objects?
- **npm/dependency story:** Can users bring external JS dependencies? If so, how?
  (ES modules from file? Bundled scripts?) Or is it pure JS only with host-provided
  capabilities for all side effects?
- **Emit API design:** What does `emit()` look like from JS? Does it buffer and flush
  on checkpoint, or write immediately? Transactional semantics?
- **Error handling:** How do JS exceptions map to the processor error lifecycle?
  Does a throw in `processRecord` trigger `onError`? Retry semantics?
- **Configuration surface:** What does the builder API look like? Sandbox defaults?
- **Hot reload:** Should file-based scripts support hot reload on change?
- **Testing story:** How do users test their JS processors? Any test harness?
- **TypeScript support:** Do we provide `.d.ts` declarations for the host API so users
  get editor support when writing processor scripts?
- **Server-side projection alignment:** KurrentDB has server-side JS projections with
  `emit()` / `linkTo()`. Should the client-side JS processor API align with that
  vocabulary, or is this a completely separate concept?
