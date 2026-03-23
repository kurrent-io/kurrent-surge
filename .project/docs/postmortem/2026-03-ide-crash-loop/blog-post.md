# How a multi-target .NET solution broke every IDE I threw at it

For almost three weeks, I couldn't use an IDE. Not Rider. Not VS Code. Nothing worked.

Rider would crash in a loop — open the solution, start working, and within minutes the backend process would
stack-overflow and die. Restart, re-analyse, crash again. Six crashes in 45 minutes on the worst night. VS Code was
a different flavor of broken: it didn't crash, it just ate 90+ GB of RAM until my MacBook Pro with 64 GB of memory
became a very expensive paperweight.

This is the story of how I figured out what was going on, and what it taught me about the limits of .NET IDE tooling.

## The setup

I work on the Kurrent .NET SDK — a multi-target C# solution with about 30 projects. We target `net8.0`, `net9.0`,
and `net10.0`. The codebase uses gRPC with Protobuf code generation, Roslyn source generators, C# 14 preview
features, and the newer `.slnx` solution format. It's a real-world production SDK, nothing exotic — just a
reasonably large .NET solution that happens to target three frameworks.

Three TFMs across 30 projects means roughly 90 output assemblies after every build. Keep that number in mind.

## "It's probably my Mac"

The crashes started sometime in February. Rider would just... die. Not immediately — I'd get maybe an hour of work
in, sometimes less, sometimes more. Random enough to shrug off. Restart Rider, keep going.

I did what anyone would do. I reinstalled Rider. I was on the latest EAP, so I rolled back one version at a time
through several releases. Same crash on all of them. I switched to VS Code with C# Dev Kit. That didn't crash, but
after a while macOS would become unresponsive. Activity Monitor showed the Roslyn language server using 90+ GB of
RAM. Force quit, reopen, repeat.

I reinstalled VS Code too. Same thing.

I started suspecting my .NET installation. Cleaned up all the preview and RC SDKs first. Then nuked everything —
wrote a script to wipe every installed SDK and reinstall only the latest stable release for .NET 8, 9, and 10.
Crashes continued.

At that point I was pretty convinced something was wrong with my machine. But I had other priorities — CI pipeline
work, other projects. So I just lived with it. Cycling between whichever IDE was less broken at the moment,
rebooting when things got too bad. Three weeks of this.

## Saturday night

By the third week of March, things had gotten worse. The crashes were no longer intermittent — they were constant.
On the evening of March 21, Rider crashed six times in 45 minutes. The crash loop was so tight the IDE was
completely unusable.

I'd had enough. Saturday night I sat down to actually figure out what the hell was going on.

I started with the error logs. Rider's backend process (`Rider.Backend` — it's a .NET application, not the JVM-based
frontend) was writing crash dumps to `~/Library/Logs/JetBrains/`. I opened one and found a 3.8 MB stack trace.

3.8 megabytes. Of stack trace. One file.

It was the same 6 lines, repeated 37,620 times:

```
at JetBrains.Util.AsyncStreamReader.ReadBuffer(System.IAsyncResult)
at System.Threading.Tasks.TaskToAsyncResult+TaskAsyncResult..ctor(...)
at System.Threading.Tasks.TaskToAsyncResult.Begin(...)
at System.IO.Pipes.PipeStream.BeginRead(Byte[], Int32, Int32, ...)
at JetBrains.Util.AsyncStreamReader.ReadBuffer(System.IAsyncResult)
...
```

## Bug #1: The pipe that ate itself

Rider's backend talks to MSBuild and the compiler through named pipes. The class that reads from these pipes —
`JetBrains.Util.AsyncStreamReader` — uses .NET's legacy `BeginRead`/`EndRead` async pattern.

Here's the problem. When `BeginRead` is called and data is already sitting in the OS pipe buffer, the operation
completes synchronously. The callback fires inline, on the same thread. That callback calls `ReadBuffer`, which
calls `BeginRead` again. If there's more data buffered — and after a build that produces 90 assemblies, there's a
lot of data — it completes synchronously again. And again. And again.

```
ReadBuffer()
 └─ PipeStream.BeginRead()          ← data ready, completes synchronously
     └─ ReadBuffer()                 ← callback fires inline, same thread
         └─ PipeStream.BeginRead()   ← still data, still synchronous
             └─ ReadBuffer()
                 └─ ...              ← 37,620 frames deep, then boom
```

Unbounded recursion. 37,620 frames until the thread stack is exhausted and the process dies.

This is a classic issue with the APM (Asynchronous Programming Model) pattern. The fix is straightforward: detect
synchronous completion and trampoline instead of recursing — use a loop, or queue the next iteration to the thread
pool. It's the kind of bug that only shows up when the pipe has enough buffered data to sustain the recursion long
enough to overflow. Like, say, after building 90 assemblies worth of output.

## Bug #2: The JIT compiler finishes the job

The second crash was weirder. Two crashes on March 22, and macOS caught them as `.ips` crash reports. The stack
trace looked nothing like the first bug:

```
libcoreclr.dylib  EEPolicy::HandleFatalStackOverflow
libcoreclr.dylib  HandleHardwareException
libcoreclr.dylib  SEHProcessException
libcoreclr.dylib  PAL_DispatchException
libcoreclr.dylib  JIT_Patchpoint(int*, int)
img11 ?? + 0      (~35 recursive frames, no symbols)
```

This one took a while to piece together.

The Rider backend scans all output DLLs to build its internal model of the solution (the "PSI" — Program Structure
Interface). With 3 TFMs per project, that's a lot of assemblies. The scanning code recurses through type hierarchies
and assembly references, and for our solution it gets deep.

While this recursion is running, .NET's tiered JIT compiler notices the method is "hot" (it's being called thousands
of times) and decides to recompile it with full optimizations using on-stack replacement (OSR). The OSR mechanism
inserts a "patchpoint" — basically swapping out the running method for the optimized version mid-execution. This swap
adds extra stack frames on top of the already-deep recursion.

The combined depth — the analysis recursion plus the JIT trampoline — overflows the stack. The process is killed by
`SIGABRT` 12 seconds after launch. Twelve seconds. The backend didn't even finish starting up.

The `img11 ??` in the stack trace means the faulting frames are JIT-compiled managed code with no on-disk image.
Can't even get the method name from the crash report. That made this one particularly fun to figure out.

## Everything I tried that didn't work

Before I found the root cause, I went through a checklist of everything that seemed reasonable:

- Disabled SWEA (Solution-Wide Error Analysis) — still crashed
- Disabled Roslyn analyzers — still crashed
- Both off together — still crashed
- Reinstalled Rider, rolled back through multiple EAP and stable versions — still crashed
- Switched to VS Code — didn't crash, ate all my RAM instead
- Reinstalled VS Code — same
- Cleaned up .NET SDKs (previews, RCs, then everything) — still crashed

None of it mattered. The crashes aren't in SWEA or the analyzers. They happen in the basic project model and
assembly scanning infrastructure — the part of the IDE that just needs to understand what's in your solution. You
can't turn that off. Without it, the IDE can't function.

## The fix (well, workaround)

Once I understood the root cause — 3 TFMs × 30 projects = 90 assemblies overwhelming the analysis layer — the fix
was obvious. Drop to a single TFM for local development:

```xml
<!-- Directory.Build.props -->
<TargetFrameworks>net10.0</TargetFrameworks>
```

Crashes stopped immediately. All 79 tests still pass. CI continues to build and test all three TFMs, so we're not
losing multi-target coverage — just local validation.

It's a livable tradeoff. But it shouldn't be necessary.

## The bigger picture

Here's what makes this interesting beyond my specific situation: this isn't a Rider bug or a VS Code bug. Both IDEs
choke on the same solution, just in different ways. Rider hits a stack limit and crashes. VS Code hits no limit and
eats all your memory instead.

Both use Roslyn under the hood. The common denominator is the solution's characteristics: the combinatorial explosion
of TFMs across a moderately large project set, combined with heavy code generation. This is a completely normal
setup for a multi-target .NET library — there's nothing weird about targeting three frameworks. But it pushes current
IDE tooling past what it can handle gracefully.

I've filed a detailed bug report with JetBrains including the crash dumps, stack traces, and root cause analysis.
I'm considering filing with the VS Code / C# Dev Kit team too. Both teams should know about this.

## What I learned

**Intermittent problems that "feel like your machine" can be real bugs.** I spent weeks assuming my Mac was the
problem. The early crashes were random enough to dismiss. The fact that they persisted across multiple Rider versions
was the clue I should have caught earlier — if every version of an application has the same bug, the bug probably
isn't in the application.

**When two unrelated tools break on the same input, look at the input.** Rider and VS Code share almost no code
beyond Roslyn. Both failing on the same solution was the strongest signal that the problem was the solution's scale,
not either IDE's implementation.

**The APM pattern is a trap.** `BeginRead`/`EndRead` with synchronous completion and inline callbacks has been a
known footgun for over a decade. It's 2026 and it's still causing stack overflows in production software. If you
have code that uses the APM pattern with recursive callbacks, go check it. Seriously.

**Debugging IDE internals is brutal and undocumented.** 3.8 MB stack traces, JIT patchpoints, macOS crash reports
with no managed symbols, a process that dies 12 seconds after launch. There are no guides for this. The only way
through is reading raw crash dumps and knowing enough about .NET runtime internals to interpret what you're seeing.
Saturday night was a long night.

---

*If your multi-target .NET solution is crashing your IDE or consuming absurd amounts of memory, try dropping to a
single TFM locally. It might save you three weeks.*
