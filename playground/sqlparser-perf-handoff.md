Hey — I need help making SqlParser-cs faster. You're in a local clone of it (C# port of Rust's sqlparser-rs, NuGet package `SqlParserCS`). 

I'm building a downstream .NET SDK (Kurrent) that parses DuckDB-dialect queries like `SELECT <cols> FROM kdb.records WHERE <pred> [LIMIT n]` on a server-side hot path, and SqlParser-cs's per-parse allocations are hurting us. 

I want to land a series of non-breaking perf PRs upstream before deciding whether to fork or write a bespoke parser.

**Please spin up a team of subagents and parallelize this work.** See the team split at the end. I want it done fast.

---

## How we got here — real numbers

I benchmarked `new Parser().ParseSql(sql, new DuckDbDialect())` against my own bespoke predicate parser. Apple M2 Max, .NET 10.0.5, BenchmarkDotNet 0.15.8, Release build, `[MemoryDiagnoser]`:

| Tier        | Bespoke parser  | SqlParserCS        | Time ratio | Alloc ratio |
|-------------|-----------------|--------------------|------------|-------------|
| 0 simple    | 144 ns / 416 B  | 5.7 µs / 10.3 KB   | 40×        | 25×         |
| 1 medium    | 693 ns / 1.8 KB | 10.0 µs / 20.4 KB  | 14×        | 11×         |
| 2 complex   | 1.6 µs / 4.9 KB | 21.4 µs / 35.2 KB  | 13×        | 7×          |

Sample inputs (wrap each in `SELECT * FROM kdb.records WHERE <predicate>` for SqlParserCS):
- Simple: `stream = 'orders'`
- Medium: `stream = 'orders' AND data.amount > 100 AND schema.name = 'OrderPlaced'`
- Complex: `stream = 'orders' AND (data.amount > 100 OR data.category LIKE 'premium%') AND data.customer.country IN ('US', 'UK', 'DE') AND timestamp > '2026-01-01'`

**Reproduce this baseline before touching anything.** Use `src/SqlParser.Benchmarks/` if it has a suitable target, or add one. Record per-tier, per-method numbers. That's the delta baseline every win measures against.

---

## Where the allocations come from

I had three explorer agents audit the codebase independently. They converged on this breakdown:

- **~80–90% AST node construction** — every AST type is a C# `record` class. Out of scope here: converting to structs is a pervasive API break and would be rejected upstream.
- **~10–15% tokenizer** — `List<char>` per identifier/number scan, `Location` is a class cloned per token, keyword lookup does `Array.IndexOf` + `ToUpperInvariant()` per identifier.
- **~5–10% parser scaffolding** — `ToSequence()` double-copies the token list, new state objects per `ParseSql` call, `new Sequence<T>()` per `ParseCommaSeparated`.

DuckDB dialect itself is a red herring — 36 lines, trivial overrides. All the perf pain is in the shared core.

---

## In-scope wins (ordered, each a separate PR)

### Win 1 — `Location` → `struct`
File: `src/SqlParser/Location.cs:4`
Currently a reference type; cloned per token at `Tokenizer.cs:42, 48`. Make it a small readonly struct. Kills one heap alloc per token (dozens per parse). Verify no code does reference-equality on `Location`.

### Win 2 — Keyword lookup → `FrozenDictionary`
File: `src/SqlParser/Tokens/Word.cs:32`
Today: `Array.IndexOf(Keywords.All, value.ToUpperInvariant())` — O(N) scan plus a `ToUpperInvariant()` string alloc per identifier. Replace with `static readonly FrozenDictionary<string, Keyword>` keyed by `StringComparer.OrdinalIgnoreCase`. Kills the scan and the upper-casing alloc.

### Win 3 — Tokenizer `List<char>` → span / pooled buffer
Files: `src/SqlParser/Tokenizer.cs:315, 331, 451, 477, 998`, `src/SqlParser/State.cs:77-96` (`PeekTakeWhile`)
Per-identifier/number/comment scans allocate a `List<char>`, spread it into a `char[]` (`[.. chars]`), then `new string(char[])`. Replace with `ReadOnlySpan<char>` slicing over the source when possible, or `ArrayPool<char>` when slicing won't fit. `StringBuilderPool.cs` already exists in the repo but is unused on hot tokenizer paths — consider routing through it where a span doesn't work.

### Win 4 — Eliminate `ToSequence()` double-copy after tokenize
File: `src/SqlParser/Parser.Sequences.cs:42`
`tokenizer.Tokenize(...).ToSequence()` copies the full `List<Token>` into a new `Sequence<Token>`. Make the tokenizer return `Sequence<Token>` directly, or reuse the backing list.

### Win 5 — `Sequence.ToSql` redundant `.ToList()`
File: `src/SqlParser/Sequence.cs:71`
`var enumerable = this.ToList()` inside `ToSql`. Drop it, iterate `this` directly with a `for` loop.

### Win 6 — Reuse per-call parser scaffolding (careful)
File: `src/SqlParser/Parser.Sequences.cs:34, 38`
`new DepthGuard` and `new ParserOptions` per `ParseSql`. Reuse where safe. Only do this if tests stay green — `DepthGuard` is probably stateful across the parse. If it's risky, skip and explain why.

### Nice-to-have (only if the first six land cleanly)
Static singletons for `Value.Null`, `Expression.Wildcard`, etc. Risky — only pursue if behavior is provably side-effect-free. Split into its own PR, last.

---

## Out of scope — do not touch

- **AST `record` → `struct` conversion.** 157 AST files, ~70 `Expression` cases. Pervasive API break. Upstream will reject.
- **`Sequence<T>` base type change.** Currently `: List<T>`. Same reason.
- **Rewriting `Parser` to be stateless / poolable.** Too invasive.
- **`DuckDbDialect`.** 36 lines of trivial overrides, nothing to optimize there.

If you spot an obviously correct structural fix that requires an API break, write it down as a note for a future major-version conversation with the maintainer. Don't implement it here.

---

## Team split (parallelize this)

Most wins touch different files, so spin up a team:

- **Agent A (fast wins, spread files):** Win 1 (`Location.cs`) + Win 4 (`Parser.Sequences.cs`) + Win 5 (`Sequence.cs`). Three small files, no conflicts.
- **Agent B (tokenizer):** Win 2 (`Tokens/Word.cs`) + Win 3 (`Tokenizer.cs` + `State.cs`). Both in the tokenizer, serialize within this agent.
- **Agent C (parser scaffolding):** Win 6 (`Parser.Sequences.cs` reuse) — start *after* Agent A finishes Win 4 to avoid a merge conflict on that file.

Use `subagent_type: general-purpose` since they're editing. Each agent must:
1. Run the existing test suite before starting — record baseline.
2. Run the benchmark — record numbers.
3. Apply the change.
4. Run tests again — must stay green.
5. Run the benchmark — record the delta.
6. Commit with the delta in the message.

Collect the deltas and report back to me in a summary table.

---

## Constraints (non-negotiable)

- **No public API changes.** Any breaking signature change is an immediate stop.
- **Test suite stays green.** `dotnet test -c Release`, zero new failures.
- **AOT-safe.** No reflection, no `Expression.Compile`, no dynamic codegen that breaks trimming.
- **Match the repo's existing style.** Check `.editorconfig`, inspect `git log` for commit message format.
- **One win per commit, one win per PR.** Small PRs get merged; bundled ones don't.
- **Each commit records its benchmark delta** in the commit body so upstream reviewers see the evidence.

---

## Success criteria

- All six wins merged locally with green tests.
- Per-tier allocation drops measurable and reproducible.
- No public API signatures changed.
- Stretch: cumulative 30–50% allocation reduction across tokenizer + parser scaffolding. AST layer will stay dominant — that's expected.

---

## First step

Reproduce the baseline benchmark, then spin up the three agents. Don't start editing before the baseline is on paper.
