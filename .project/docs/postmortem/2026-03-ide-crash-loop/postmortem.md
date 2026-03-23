# Postmortem: IDE crash loop on multi-TFM .NET solution

**Duration**: weeks, possibly longer (intermittent since at least February 2026 → March 22, 2026)
**Severity**: Dev environment completely degraded — no stable IDE for local development
**Resolved**: March 22, 2026 (workaround applied)

## What happened

Both major .NET IDEs became unusable on our solution:

- **Rider** entered a crash → restart → re-analyse → crash loop. 20+ backend crashes logged, up to 6 in a single
  session. The backend process (`Rider.Backend`) stack-overflowed repeatedly during assembly scanning and pipe I/O.
- **VS Code** (C# Dev Kit) didn't crash, but the Roslyn language server consumed 90+ GB of RAM until macOS became
  unresponsive. Force-quitting and restarting bought a few minutes before it happened again.

No stable IDE meant switching between whichever one was less broken at the moment, constantly restarting processes,
and losing hours of focus time.

## Timeline

**February–early March (weeks)**: Rider starts crashing intermittently. At first it seems random — happens after a
while, not immediately. Assumed it was a macOS or hardware issue. Uninstalled and reinstalled Rider multiple times,
tried several different Rider versions. Same behavior on all of them. Also tried VS Code, which didn't crash but
gradually consumed all available memory. Uninstalled and reinstalled VS Code too. Nothing helped.

Also cleaned up all installed .NET SDKs — first removed preview and RC versions, then nuked everything. Wrote a
script to wipe all SDKs and reinstall only the latest stable release for each major version (8, 9, 10). Crashes
persisted.

The intermittent nature made it easy to dismiss — restart the IDE, get another hour of work in, repeat. It wasn't
until the crashes became frequent and predictable that it became clear this was a real, reproducible bug.

**Early March onward**: Crashes become more frequent. No time to investigate the root cause — other priorities (CI
pipeline work, other projects). Day-to-day survival was cycling between VS Code until the Mac ran out of memory,
and Rider until it crashed. Reboot, repeat.

**March 18**: Crash frequency increases noticeably. First crash logs preserved from this date forward.

**March 21 (evening)**: 6 identical Rider crashes between 22:19 and 23:04. The crash loop becomes tight enough that
Rider is completely unusable. Error logs show 37,620 identical stack frames — `AsyncStreamReader.ReadBuffer`
recursing through a named pipe read.

**March 22 (Saturday night)**: Deep debugging session. Reverse-engineered two distinct crash paths inside Rider's
backend:

1. **AsyncStreamReader pipe recursion** — the legacy `BeginRead`/`EndRead` async pattern completing synchronously
   and recursing unboundedly. 37,620 frames deep.
2. **JIT patchpoint overflow** — .NET's tiered JIT trying to on-stack-replace a method that's already deep in
   recursion during assembly scanning. Backend killed 12 seconds after launch.

Tried disabling SWEA (Solution-Wide Error Analysis). Crashes continued.
Tried disabling Roslyn analyzers. Still crashed.
Both off together. Still crashed.

Root cause identified: 3 TFMs × ~30 projects = ~90 output assemblies. The sheer volume of analysis work overwhelms
both IDEs' internals. Rider overflows its stack. VS Code overflows memory.

**Resolution**: dropped to a single TFM (`net10.0`) for local development. Crashes stopped immediately. All 79 tests
pass. CI continues to build all 3 TFMs.

**March 23**: Bug report finalized and filed with JetBrains. Team notified.

## Root cause

The solution targets 3 frameworks (`net8.0`, `net9.0`, `net10.0`), producing ~90 output assemblies across ~30
projects. Add gRPC/Protobuf code generation, Roslyn source generators (TUnit), and C# 14 preview features on top.

Both Rider and VS Code use Roslyn for C# analysis. Neither IDE scales gracefully when the number of TFM × project
combinations gets this high. The specific crash mechanisms are different — Rider hits stack limits in two internal
code paths, VS Code's language server hits no limits and consumes all available memory — but the underlying problem
is the same: the analysis layer can't handle this workload.

Disabling SWEA and Roslyn analyzers didn't help because the crashes happen in the basic project model and assembly
scanning infrastructure, not in optional analysis features.

## Impact

- **~3 weeks of degraded development** — no stable IDE for local work
- **Constant context switching** — cycling between Rider and VS Code, restarting processes, rebooting macOS when
  memory exhausted
- **Reduced velocity** on all work touching this solution during the period
- **Diagnostic effort** — the Saturday night session to find the root cause was several hours of reading crash dumps,
  3.8 MB stack traces, macOS `.ips` files, and .NET runtime internals

## Resolution

**Workaround applied**: single TFM (`net10.0`) for local development.

```xml
<!-- Directory.Build.props -->
<TargetFrameworks>net10.0</TargetFrameworks>
```

CI still builds and tests all 3 TFMs, so multi-target coverage isn't lost.

## Action items

| Action                                         | Status |
|------------------------------------------------|--------|
| File bug report with JetBrains (Rider crashes) | Done   |
| Share findings with team                       | Done   |
| Apply single-TFM workaround to local dev       | Done   |
| Monitor for Rider fix in future releases       | Open   |
| Consider filing with VS Code / C# Dev Kit team | Open   |

## Lessons learned

**The symptom wasn't the cause.** SWEA, Roslyn analyzers, and C# 14 preview were all suspects early on. None of
them were the problem. The actual trigger was the combinatorial explosion of 3 TFMs across 30 projects — something
that's entirely normal for a multi-target .NET solution but apparently at the edge of what current IDE tooling
handles.

**When two unrelated tools break on the same input, the input is the problem.** Rider and VS Code share almost no
code except Roslyn. Both choking on the same solution pointed to the solution's characteristics, not either IDE's
implementation.

**Intermittent problems that "feel like your machine" can be real bugs.** The early crashes were random enough to
blame on macOS or hardware. Reinstalling both IDEs multiple times, trying different Rider versions — none of it
helped because the problem was never the IDE installation. The fact that it persisted across multiple Rider versions
was a clue that should've pointed away from the IDE and toward the solution earlier.

**Debugging IDE internals is brutal.** 3.8 MB stack traces, JIT compiler patchpoints, macOS crash reports with no
managed symbols, a backend process that dies 12 seconds after launch. None of this is documented anywhere. The only
way through was reading raw crash dumps.
