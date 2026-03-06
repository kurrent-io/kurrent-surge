# QueryExpressionNormalizer Spec

## Purpose

Validates and rewrites SQL query expressions in a zero-allocation, SIMD-accelerated hot path. Three responsibilities:

1. **Reject** queries referencing forbidden table names (exact match, any position).
2. **Reject** queries where a table name starts with a forbidden prefix (position-aware — table names only, not columns/aliases).
3. **Rewrite** table and identifier names according to a replacement map.

## API

```csharp
public sealed record NormalizerOptions {
    public ImmutableArray<string>                       ForbiddenTables      { get; init; } = [];
    public ImmutableArray<KeyValuePair<string, string>> Replacements         { get; init; } = [];
    public string?                                      ForbiddenTablePrefix { get; init; }
}

public readonly ref struct NormalizeResult {
    public ReadOnlySpan<char> Value   { get; init; }
    public bool               IsValid { get; init; }
    public string?            Error   { get; init; }

    public static NormalizeResult Success(ReadOnlySpan<char> normalized);
    public static NormalizeResult Failure(string error);
}

public sealed class QueryExpressionNormalizer(NormalizerOptions options) {
    /// Validates and rewrites a query in a single pass.
    /// Returns immediately on the first violation.
    /// Buffer must be large enough — query.Length * 2 is a safe upper bound.
    public NormalizeResult Normalize(ReadOnlySpan<char> query, Span<char> buffer);
}
```

### Design Notes

- **Thread-safe** — immutable after construction. No shared mutable state.
- **Matching is case-insensitive** (`OrdinalIgnoreCase`) for both exact and prefix checks.
- **Fail-fast** — the scanner returns immediately on the first violation. On a hot path, scanning the remainder of a query that will be rejected is wasted work. The `Error` property carries a human-readable description of the violation.
- **`NormalizeResult` is a `ref struct`** — it cannot escape the calling method's scope, which is intentional since `Value` is a span into the caller-provided buffer. The internal `Scan` method returns `string?` (error or null) with `out int written` to avoid ref-struct-in-scope issues with `stackalloc` buffers. `Normalize` constructs the `NormalizeResult` outside all temporary scopes.
- **Options pattern** — `NormalizerOptions` is a `record` for extensibility. Adding features (e.g. `NestedBlockComments = true`) doesn't break existing callers.

## Exact-Match Forbidden Tables

Checked against **all identifiers** regardless of SQL position. If a name appears in the forbidden set, it's rejected whether it appears after `FROM`, `SELECT`, `WHERE`, or anywhere else.

This is intentionally broad — if a table name is forbidden, any reference to it is suspicious.

### Identifier Forms

| Form            | Example             | Detection                                              |
|-----------------|---------------------|--------------------------------------------------------|
| Bare identifier | `FROM secrets`      | Scanned as contiguous identifier chars                 |
| Bracket-quoted  | `FROM [secrets]`    | Content between `[` and `]` extracted and checked      |
| Double-quoted   | `FROM "secrets"`    | Content between `"` delimiters extracted and checked   |
| `]]` escape     | `FROM [sec]]rets]`  | Unescaped to `sec]rets` before checking                |
| `""` escape     | `FROM "my""table"`  | Unescaped to `my"table` before checking                |
| Unicode escape  | `FROM \u0073ecrets` | `\uXXXX` sequences decoded in pre-pass before scanning |

### Skipped Regions (No False Positives)

| Region              | Example         | Behavior                                 |
|---------------------|-----------------|------------------------------------------|
| Single-line comment | `-- secrets`    | Skipped to end of line                   |
| Block comment       | `/* secrets */` | Skipped to closing `*/`                  |
| String literal      | `'secrets'`     | Skipped, including `''` escape sequences |
| N-prefixed string   | `N'secrets'`    | `N` consumed, literal skipped            |

## Prefix-Based Forbidden Tables

Checked **only in table-name positions**. A column named `kdb_status` in a `SELECT` or `WHERE` clause must not trigger rejection.

### Configuration

A single prefix string (e.g. `"kdb_"`) via `NormalizerOptions.ForbiddenTablePrefix`. When set, any identifier in a table-name position that starts with this prefix (case-insensitive) causes `Normalize` to return a failure result.

When `ForbiddenTablePrefix` is null, the prefix check is skipped entirely — no overhead.

### Table-Name Positions

An identifier is in table-name position when it follows a table-introducing keyword, accounting for parenthesis depth and alias disambiguation.

**Table-introducing keywords:**

`FROM`, `JOIN`, `INTO`, `UPDATE`, `TABLE`

**JOIN modifier keywords (transparent — do not consume or alter state):**

`LEFT`, `RIGHT`, `INNER`, `OUTER`, `FULL`, `CROSS`, `NATURAL`, `LATERAL`

**Context-resetting keywords:**

`SELECT`, `WHERE`, `ON`, `SET`, `ORDER`, `GROUP`, `HAVING`, `LIMIT`, `UNION`, `VALUES`, `RETURNING`

### State Machine

The scanner maintains a state per parenthesis depth level:

```
┌─────────┐   FROM/JOIN/INTO/        ┌─────────────────┐
│ Default │──────────────────────────▶│ ExpectingTable  │
└─────────┘   UPDATE/TABLE           └────────┬────────┘
     ▲                                        │
     │                                   (identifier)
     │                              [prefix check here]
     │                                        │
     │                                        ▼
     │                               ┌────────────────┐
     │    (keyword/                   │   AfterTable   │
     │     end of context)           └───┬────┬───┬───┘
     │◀──────────────────────────────────┘    │   │
     │                                   AS   │   │ ,
     │                                        ▼   │
     │                              ┌──────────┐  │
     │    (identifier)              │ExpectAlias│  │
     │◀─────────────────────────────┤          │  │
     │                              └──────────┘  │
     │                                            │
     │                               ┌────────────┘
     │                               ▼
     │                      ┌─────────────────┐
     └──────────────────────│ ExpectingTable  │  (back to expecting
                            └─────────────────┘   next table in list)
```

**State transitions:**

| Current State    | Token                                         | Next State                      | Action               |
|------------------|-----------------------------------------------|---------------------------------|----------------------|
| `Default`        | `FROM` / `JOIN` / `INTO` / `UPDATE` / `TABLE` | `ExpectingTable`                | —                    |
| `Default`        | JOIN modifier (`LEFT`, `RIGHT`, etc.)         | `Default`                       | —                    |
| `Default`        | any other identifier                          | `Default`                       | —                    |
| `ExpectingTable` | identifier                                    | `AfterTable`                    | **Check prefix**     |
| `ExpectingTable` | `(`                                           | push depth → `Default`          | Subquery entered     |
| `ExpectingTable` | JOIN modifier                                 | `ExpectingTable`                | Transparent          |
| `AfterTable`     | `AS`                                          | `ExpectingAlias`                | —                    |
| `AfterTable`     | identifier (not keyword)                      | `Default`                       | Implicit alias, skip |
| `AfterTable`     | `,`                                           | `ExpectingTable`                | Multi-table FROM     |
| `AfterTable`     | `JOIN` / `WHERE` / `ON` / etc.                | transition per keyword          | —                    |
| `ExpectingAlias` | identifier                                    | `Default`                       | Alias consumed, skip |
| any state        | `)`                                           | pop depth → restore outer state | Subquery exited      |

### Parenthesis Depth Tracking

Each `(` pushes the current state onto a stack and resets to `Default`. Each `)` pops and restores the outer state.

This handles:

- `FROM (SELECT ... FROM kdb_internal) AS t` — inner `kdb_internal` is in table position at depth 1, correctly checked.
- `WHERE x IN (SELECT ...)` — subquery has independent state.
- `FROM a JOIN (SELECT ...) AS b ON ...` — subquery state is independent from outer `JOIN` context.

Stack is `stackalloc State[32]`. Queries exceeding 32 levels of nesting are rejected with an error — this avoids heap allocation and is well beyond any practical query depth.

## Table Name Replacement

When an identifier (in any position) matches a key in the replacement dictionary, it is replaced by the corresponding value in the output. Replacement preserves the original quoting style.

| Input                 | Replacement Map      | Output      |
|-----------------------|----------------------|-------------|
| `$idx-all` (bare)     | `$idx-all → records` | `records`   |
| `[$idx-all]`          | `$idx-all → records` | `[records]` |
| `"$idx-all"`          | `$idx-all → records` | `"records"` |
| `my_table` (no match) | —                    | `my_table`  |

Replacements are checked **after** the forbidden table check. If an identifier is forbidden, `Normalize` returns a failure result without attempting replacement.

Replacement applies to **all identifier positions** — not just table names. This is consistent with the exact-match forbidden check.

## Evaluation Order

For each identifier encountered during scanning, `Normalize` evaluates in this order:

1. **Exact forbidden check** — if the identifier matches the `FrozenSet`, return failure. Applies to all positions.
2. **Prefix forbidden check** — if `ForbiddenTablePrefix` is configured AND the identifier is in a table-name position AND it starts with the prefix, return failure.
3. **Replacement check** — if the identifier matches the replacement dictionary, write the replacement value (preserving quoting). Otherwise copy the original.

All three checks are unified in a single `ResolveIdentifier` method, called from bare identifiers, non-escaped delimited identifiers, and escaped delimited identifiers alike.

## Performance Characteristics

### Zero Allocation (Happy Path)

- All scanning operates on `ReadOnlySpan<char>`.
- `FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>>` enables span-based exact lookup without string materialization.
- `FrozenDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>>` enables span-based replacement lookup. Initialized unconditionally — `TryGetValue` on an empty `FrozenDictionary` returns `false` immediately, avoiding a guard branch on every identifier.
- Prefix check uses `ReadOnlySpan<char>.StartsWith(prefix, OrdinalIgnoreCase)`. When `ForbiddenTablePrefix` is null, the check is a single null comparison.
- Escaped identifiers (`]]`, `""`) and unicode normalization use `stackalloc` for buffers ≤256 chars, `ArrayPool<char>` for larger.
- Parenthesis state stack uses `stackalloc State[32]`. Queries exceeding this depth are rejected.
- Error path allocates a single `string` for the error message. Happy path is fully zero-alloc.

### SIMD Acceleration

- `Span.IndexOf` calls (skip helpers, delimiter search) are vectorized by the runtime.
- Unicode pre-pass gated behind `query.Contains('\\')` — SIMD-accelerated, effectively free for the common case.
- Prefix check via `MemoryExtensions.StartsWith` is also vectorized for longer prefixes.

### Keyword Recognition

Keywords are recognized from bare identifiers by length-bucketed comparison. No dictionary lookup — a `switch` on `span.Length` then `MemoryExtensions.Equals` with `OrdinalIgnoreCase`. The JIT optimizes this into efficient comparisons for the small fixed set of SQL keywords.

## Code Organization

The implementation uses local functions within `Scan` for helpers that are only needed there:

- `IsIdentifierStart`, `IsIdentifierPart` — character classification.
- `SkipLineComment`, `SkipBlockComment`, `SkipStringLiteral` — SIMD-accelerated region skippers.

Class-level methods are limited to those shared across `Scan` and the delimited identifier handlers:

- `Peek` — bounds-checked single-char lookahead.
- `CopyRange` — span-to-span copy with write cursor.
- `Unescape` — `]]`/`""` escape removal.
- `ResolveIdentifier` — unified forbidden check → replacement → copy.
- `HandleDelimited` — delimited identifier parsing (brackets and double quotes).
- `ResolveEscapedIdentifier` — stackalloc + unescape + delegate to `ResolveIdentifier`.
- `CheckForbidden` — exact match + prefix check, returns `string?` error.
- `TransitionKeyword` / `ClassifyKeyword` — state machine transitions.
- `AdvanceState` — simplified transition for delimited identifiers (which cannot be keywords).
- `PushState` / `PopState` — parenthesis depth stack.
- `DecodeUnicodeEscapes` / `TryDecodeUnicodeEscape` — `\uXXXX` pre-pass.

## Edge Cases

| Input                             | Prefix | Result | Reason                                     |
|-----------------------------------|--------|--------|--------------------------------------------|
| `SELECT kdb_col FROM t`           | `kdb_` | pass   | `kdb_col` is in SELECT, not table position |
| `FROM kdb_internal`               | `kdb_` | reject | Table position, prefix match               |
| `FROM t WHERE kdb_col = 1`        | `kdb_` | pass   | `kdb_col` in WHERE, not table position     |
| `FROM a, kdb_internal`            | `kdb_` | reject | Comma in FROM, still table position        |
| `FROM (SELECT * FROM kdb_x) AS t` | `kdb_` | reject | `kdb_x` in table position at depth 1       |
| `JOIN kdb_lookup ON ...`          | `kdb_` | reject | `JOIN` introduces table position           |
| `LEFT JOIN kdb_lookup ON ...`     | `kdb_` | reject | `LEFT` is transparent, `JOIN` triggers     |
| `FROM t AS kdb_alias`             | `kdb_` | pass   | `kdb_alias` is alias, not table name       |
| `FROM t kdb_alias`                | `kdb_` | pass   | Implicit alias after table name            |
| `INSERT INTO kdb_sink`            | `kdb_` | reject | `INTO` introduces table position           |
| `UPDATE kdb_target SET ...`       | `kdb_` | reject | `UPDATE` introduces table position         |
| `WHERE name = 'kdb_value'`        | `kdb_` | pass   | Inside string literal                      |
| `-- FROM kdb_hidden`              | `kdb_` | pass   | Inside comment                             |
| `((((... 33 deep ...))))`         | any    | reject | Exceeds max nesting depth (32)             |

## Not In Scope

- **Nested block comments** — `/* /* */ */` (SQL Server extension, not standard SQL). Can be added with a depth counter.
- **Schema qualification** — `dbo.secrets` is not split on `.`. The scanner sees `dbo` and `secrets` as separate bare identifiers. For the exact-match check this works correctly (`.` is not an identifier character, so `secrets` is still caught). For the prefix check, `dbo` is seen as the table name in table-position context; `secrets` following `.` would be a second identifier. If schema-qualified prefix checking is needed later, `.` handling must be added.
- **Dynamic SQL / `EXEC`** — string concatenation that builds table names at runtime cannot be caught by lexical scanning.
- **Surrogate pairs** — only BMP characters (single `char`) are decoded from `\uXXXX`.
- **`WITH` (CTEs)** — CTE names after `WITH` are not currently tracked. The CTE body's `FROM` clauses are handled normally.
- **Collecting all errors** — the scanner bails on the first violation for performance. If diagnostic use cases arise, a `bool collectAll` parameter could be added later.
