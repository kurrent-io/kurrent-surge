# Rider 2025.3.3 — Backend crashes with stack overflows on multi-target solutions

## What's happening

Rider's .NET backend (`Rider.Backend`, the ReSharper host) is stack-overflowing repeatedly.
Two separate code paths, both internal to the backend — no user code on the stack.

20+ crashes since 2026-03-18. 6 in one evening session. The backend crashes, restarts,
re-analyses, and crashes again. IDE becomes unusable.

## Terminology

If you're not familiar with these, the links go to official docs.

| Term                 | What it is                                                                                                                                                                                                                                                  |
|----------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **SWEA**             | [Solution-Wide Error Analysis](https://www.jetbrains.com/help/rider/Code_Analysis__Solution-Wide_Analysis.html). Rider's background analysis that re-scans the whole solution after changes and builds.                                                     |
| **TFM**              | [Target Framework Moniker](https://learn.microsoft.com/dotnet/standard/frameworks). An identifier like `net8.0`. Multi-target projects compile once per TFM, so a project targeting 3 TFMs produces 3 separate output DLLs.                                 |
| **Tiered JIT**       | [Tiered compilation](https://learn.microsoft.com/dotnet/core/runtime-config/compilation#tiered-compilation). .NET compiles methods quickly at first, then re-compiles hot methods with full optimizations.                                                  |
| **Patchpoint / OSR** | [On-Stack Replacement](https://github.com/dotnet/runtime/blob/main/docs/design/features/OnStackReplacement.md). The JIT can swap a running method mid-execution. The swap inserts extra stack frames — bad news if the method is already deep in recursion. |
| **PSI**              | Program Structure Interface. ReSharper's internal model of the loaded solution's types and syntax.                                                                                                                                                          |

## Environment

### Machine

|           |                                    |
|-----------|------------------------------------|
| **Model** | MacBook Pro (Mac14,5)              |
| **CPU**   | Apple M2 Max, ARM64                |
| **RAM**   | 64 GB                              |
| **OS**    | macOS 15.4 Sequoia (Darwin 25.3.0) |

### IDE

|                  |                                                  |
|------------------|--------------------------------------------------|
| **Rider**        | 2025.3.3, Build #RD-253.31033.136                |
| **Install path** | `~/Applications/Rider.app` (Toolbox) |

### .NET runtimes and SDKs

|                           |                              |
|---------------------------|------------------------------|
| **Installed SDKs**        | 8.0, 9.0, 10.0.201 (preview) |
| **Rider.Backend runtime** | .NET 9.0.11, ARM64           |

The backend runtime ships inside Rider — it's not user-configurable. And because
`Rider.Backend` is a CoreCLR process (not JVM), `rider.vmoptions` has no effect on its
stack size. There's no knob for this.

### Solution

|              |                                                                                                                       |
|--------------|-----------------------------------------------------------------------------------------------------------------------|
| **Format**   | [`.slnx`](https://learn.microsoft.com/visualstudio/ide/reference/solution-file?view=vs-2022) (newer XML-based format) |
| **Projects** | ~30                                                                                                                   |
| **TFMs**     | `net8.0`, `net9.0`, `net10.0` — each library compiles 3x                                                              |
| **Language** | C# 14 ([`LangVersion: preview`](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2252))  |
| **Codegen**  | gRPC/Protobuf via `Grpc.Tools`, Roslyn source generators (TUnit)                                                      |

3 TFMs across ~30 projects = roughly **90 output assemblies** the backend has to index
after every build, plus all their generated source files.

---

## Crash 1 — AsyncStreamReader pipe recursion

**6 identical crashes** on 2026-03-21 between 22:19 and 23:04.

### The bug

`Rider.Backend` talks to child processes (MSBuild, compilers) over named pipes. The reader
class — `JetBrains.Util.AsyncStreamReader` — uses the legacy [`BeginRead`/`EndRead`](https://learn.microsoft.com/dotnet/api/system.io.stream.beginread) pattern.

The problem: when data is already sitting in the OS pipe buffer, `BeginRead` completes
synchronously and fires the callback on the same thread. That callback calls `ReadBuffer`,
which calls `BeginRead`, which completes synchronously again. Unbounded recursion.

```
ReadBuffer()
 └─ PipeStream.BeginRead()          ← data ready, completes synchronously
     └─ ReadBuffer()                 ← callback fires inline, same thread
         └─ PipeStream.BeginRead()   ← still data, still synchronous
             └─ ReadBuffer()
                 └─ ...              ← 37,620 frames deep, then boom
```

37,620 frames. The 3.8 MB log file is the same 6-line block repeated thousands of times.

### Stack trace (trimmed)

```
Stack overflow.
   at System.Text.StringBuilder.ToString(Int32, Int32)
   at JetBrains.Util.AsyncStreamReader.GetLinesFromStringBuilder()
   at JetBrains.Util.AsyncStreamReader.ReadBuffer(System.IAsyncResult)
   at System.Threading.Tasks.TaskToAsyncResult+TaskAsyncResult..ctor(...)
   at System.Threading.Tasks.TaskToAsyncResult.Begin(...)
   at System.IO.Pipes.PipeStream.BeginRead(Byte[], Int32, Int32, ...)
   at JetBrains.Util.AsyncStreamReader.ReadBuffer(System.IAsyncResult)
   ... [37,618 more identical frames]
```

### What triggers it

The backend log shows 242 files queued for re-analysis after building
`Kurrent.Testkit.Tests.csproj`. The last file being processed was a generated Protobuf
file at `obj/Debug/net8.0/v1/Projections.cs`. However, disabling SWEA did not prevent the
crashes (see "What we tried"), which suggests the trigger is the broader assembly and
project model scanning, not SWEA specifically.

### Fix

`ReadBuffer` needs a trampoline. If `BeginRead` completes synchronously, don't recurse —
loop, or queue the next read to the thread pool. Classic APM callback issue.

---

## Crash 2 — JIT patchpoint during assembly scanning

**2 crashes** on 2026-03-22 (00:44 and 01:18). The second one killed the backend **12
seconds after it launched** — started at 01:18:10, dead at 01:18:22.

### The bug

The backend scans output DLLs to build its PSI model. With 3 TFMs per project, that's
a lot of assemblies, and the scanning code recurses through type hierarchies and assembly
references.

While that recursion is already deep, .NET's tiered JIT kicks in with an on-stack
replacement — it decides the method is hot and tries to swap it out mid-execution. The OSR
trampoline adds more frames on top of the already-deep recursion. Stack overflows.

### Stack trace

From macOS crash report `Rider.Backend-2026-03-22-011823.ips`:

```
Exception Type:  EXC_CRASH (SIGABRT)
Termination:     Abort trap: 6
Faulting Thread:  76

Thread 76:
  libcoreclr.dylib  EEPolicy::HandleFatalStackOverflow + 716
  libcoreclr.dylib  HandleHardwareException + 712
  libcoreclr.dylib  SEHProcessException + 344
  libcoreclr.dylib  PAL_DispatchException + 100
  libcoreclr.dylib  JIT_Patchpoint(int*, int) + 176
  img11 ?? + 0      (~35 recursive frames of JIT'd managed code, no symbols)
```

`img11` is in-memory JIT-compiled code — no mapped image, so we can't get the managed
method name from the crash report. It's ReSharper's internal analysis code.

### What triggers it

`ModuleReferenceResolveSync` → `AssemblyPsiModuleFactory`, processing output DLLs for
multi-target projects (`Kurrent.Client` and `Kurrent.Variant`, each with 3 TFMs).

### Fix

The recursion depth in the assembly scanner isn't bounded. The JIT patchpoint is the
finishing blow, but the real problem is that the recursion gets deep enough to be in
the danger zone in the first place. Recursion guards or larger thread stacks for the
analysis workers would address it.

---

## Reproducing it

1. Open a multi-target .NET solution (3+ TFMs, ~30 projects, gRPC codegen, Roslyn
   source generators) in Rider 2025.3.3
2. SWEA on (it's the default)
3. Build any project
4. Backend crashes within seconds

Heavier solutions crash more reliably — more TFMs, more generated files, more output
assemblies to scan.

## What we tried

**Didn't work:**

- Turning off SWEA — crashes kept happening
- Disabling Roslyn analyzers on top of SWEA — still crashed
- Both off together — still crashed
- Reinstalling Rider multiple times — started on the latest EAP, then rolled back one
  version at a time through several releases. Same behavior on all of them
- Reinstalling VS Code — language server still consumed 90+ GB RAM
- Cleaning up all .NET SDKs — first removed previews and RCs, then nuked everything and
  reinstalled only the latest stable release per major version (8, 9, 10) — still crashed

**Only thing that worked:** dropping to a single TFM.

```xml
<!-- before: 3 TFMs, ~90 output assemblies -->
<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>

<!-- after: 1 TFM, ~30 output assemblies, no more crashes -->
<TargetFrameworks>net10.0</TargetFrameworks>
```

All 79 tests still pass with a single TFM. We run all 3 TFMs in CI so we're not losing
coverage — just losing local multi-target validation. It's a livable tradeoff, but we
shouldn't have to make it.

## Not just Rider — VS Code chokes on the same solution

This isn't Rider-specific. The same solution brought VS Code (C# Dev Kit / Roslyn language
server) to its knees too — it didn't crash with stack overflows, but the Roslyn language
server consumed 90+ GB of RAM until macOS became completely unresponsive.

Both IDEs use Roslyn for C# analysis. The common denominator is the solution itself: 3 TFMs
× ~30 projects = ~90 distinct analysis contexts, heavy gRPC/Protobuf codegen, Roslyn source
generators, and C# 14 preview features. That scale of analysis work overwhelms the tooling
in both environments — Rider hits a stack limit and crashes, VS Code hits no limit and eats
all available memory instead.

We're filing this against Rider because the stack overflow crash paths are concrete and
reproducible, but the underlying problem likely lives in the shared Roslyn analysis layer
or in how both IDEs scale their analysis across a large number of TFM × project combinations.

## A note on redaction

Home directory paths in the attached `.log` files have been redacted using the same
convention macOS applies in `.ips` crash reports: `/Users/<username>/Applications/` →
`/Users/USER/*/`. The `.ips` files were already redacted by macOS automatically.

## Attached files

| File                                    | Size   | What's in it                                                           |
|-----------------------------------------|--------|------------------------------------------------------------------------|
| `Rider.Backend-2026-03-22-011823.ips`   | ~87 KB | macOS crash report — JIT patchpoint overflow at 01:18                  |
| `Rider.Backend-2026-03-22-004449.ips`   | ~87 KB | macOS crash report — earlier patchpoint crash at 00:44                 |
| `backend.2026-03-21_2248.39239-err.log` | 3.8 MB | Backend error log — the 37,620-frame AsyncStreamReader recursion       |
| `backend.2026-03-22_0118.59279-err.log` | 252 B  | Backend error log — latest crash, stack overflow with no managed trace |
