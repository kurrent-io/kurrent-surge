# Expression Filter Virtual Columns Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add `category` and `index` virtual columns to the expression filter, expand column aliases to spec v2
parity, and remove prefix matching in favor of direct `$idx-*` stream targeting.

**Architecture:** Extend `PropertyAccess.Resolve` with new aliases and category/index resolution. Extend
`ParsePropertyAccess` to handle multi-segment dot paths (`rec.schema.name`). Extend `ServerFilterExtractor` with
category and index extraction priorities. Change `ByCategory` factory to target `$idx-ce-*` streams. Remove
`ByStreamPrefix`, `BySchemaPrefix`, and the `exactMatch` parameter from `RegexFilter`.

**Tech Stack:** C# 14, TUnit, Pratt parser (hand-written in `ExpressionFilter.Parser.cs`)

## Contents

- [Task 1: Add simple aliases to PropertyAccess.Resolve](#task-1-add-simple-aliases-to-propertyaccessresolve)
- [Task 2: Add category derivation to PropertyAccess.Resolve](#task-2-add-category-derivation-to-propertyaccessresolve)
- [Task 3: Add index virtual column to PropertyAccess.Resolve](#task-3-add-index-virtual-column-to-propertyaccessresolve)
- [Task 4: Extend ParsePropertyAccess for multi-segment dot paths](#task-4-extend-parsepropertyaccess-for-multi-segment-dot-paths)
- [Task 5: Add rec.* aliases to PropertyAccess.Resolve](#task-5-add-rec-aliases-to-propertyaccessresolve)
- [Task 6: Add field_ prefix stripping](#task-6-add-field_-prefix-stripping)
- [Task 7: Add category extraction to ServerFilterExtractor](#task-7-add-category-extraction-to-serverfilterextractor)
- [Task 8: Add index extraction to ServerFilterExtractor](#task-8-add-index-extraction-to-serverfilterextractor)
- [Task 9: Update ByCategory to target $idx-ce-* streams](#task-9-update-bycategory-to-target-idx-ce--streams)
- [Task 10: Remove prefix matching support](#task-10-remove-prefix-matching-support)

## Reference files

- Source: `src/Kurrent.Client/Modules/Streams/Models/Filters/ExpressionFilter.Parser.cs`
- Source: `src/Kurrent.Client/Modules/Streams/Models/Filters/ExpressionFilter.ServerExtractor.cs`
- Source: `src/Kurrent.Client/Modules/Streams/Models/Filters/RecordFilter.cs`
- Source: `src/Kurrent.Client/Modules/Streams/Models/Filters/RegexFilter.cs`
- Source: `src/Kurrent.Client/Modules/Streams/Models/Filters/StreamFilter.cs`
- Source: `src/Kurrent.Client/Modules/Streams/Models/Filters/SchemaFilter.cs`
- Tests: `test/Kurrent.Client.Tests/Filters/ExpressionFilterParserTests.cs`
- Tests: `test/Kurrent.Client.Tests/Filters/ExpressionFilterTests.cs`
- Tests: `test/Kurrent.Client.Tests/Filters/ServerFilterExtractorTests.cs`
- Tests: `test/Kurrent.Client.Tests/Filters/StreamFilterTests.cs`
- Tests: `test/Kurrent.Client.Tests/Filters/SchemaFilterTests.cs`
- Tests: `test/Kurrent.Client.Tests/Filters/RecordFilterTests.cs`
- Design: [expression-filter-virtual-columns-design.md](2026-03-03-expression-filter-virtual-columns-design.md)

## Test runner

All test commands use TUnit via `dotnet run`, not `dotnet test`:

```sh
# run specific test class
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/ExpressionFilterParserTests/*"

# run specific test method
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/ExpressionFilterParserTests/parses_event_type_alias"
```

## Task 1: Add simple aliases to PropertyAccess.Resolve

Add `event_type`, `event_number`, `log_position`, `created_at`, `created_ts`, and `metadata` aliases to the
existing `PropertyAccess.Resolve` switch expression.

**Files:**

- Modify: `src/Kurrent.Client/Modules/Streams/Models/Filters/ExpressionFilter.Parser.cs` (PropertyAccess.Resolve)
- Modify: `test/Kurrent.Client.Tests/Filters/ExpressionFilterParserTests.cs`

**Step 1: Write failing tests**

Add tests to `ExpressionFilterParserTests.cs` that parse expressions using the new aliases and verify they
resolve to the correct record fields. Follow the existing test patterns in the file. Tests needed:

- `event_type = 'OrderPlaced'` resolves same as `schema = 'OrderPlaced'`
- `event_number > 5` resolves same as `revision > 5`
- `log_position > 1000` resolves same as `position > 1000`
- `created_at >= '2026-01-01'` resolves same as `timestamp >= '2026-01-01'`
- `created_ts >= '2026-01-01'` resolves same as `timestamp >= '2026-01-01'`
- `metadata.region = 'us-east'` resolves same as `properties.region = 'us-east'`
- `stream_name = 'orders-99'` resolves same as `stream = 'orders-99'`
- `record_id` already exists; confirm `id` still works

**Step 2: Run tests to verify they fail**

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/ExpressionFilterParserTests/*"
```

Expected: new tests FAIL because the aliases are not recognized (resolve to `null`).

**Step 3: Implement aliases in PropertyAccess.Resolve**

In `ExpressionFilter.Parser.cs`, expand the `PropertyAccess.Resolve` switch:

```csharp
public object? Resolve(Record record) =>
    PropertyName.ToLowerInvariant() switch {
        "stream" or "stream_name"                        => record.Stream.Value,
        "schema_name" or "schema" or "event_type"        => record.Schema.SchemaName.Value,
        "timestamp" or "ts" or "created_at" or "created_ts"
                                                         => record.Timestamp,
        "position" or "pos" or "log_position"            => record.Position.LogPosition.Value,
        "stream_revision" or "revision" or "event_number"
                                                         => (long)record.Revision,
        "record_id" or "id"                              => record.Id,
        "properties" or "props" or "metadata"            => Key is null
            ? null
            : record.Metadata.TryGetValue<object>(Key, out var value)
                ? value
                : null,
        _ => null
    };
```

**Step 4: Run tests to verify they pass**

Same command as step 2. Expected: all new and existing tests PASS.

**Step 5: Commit**

```sh
git commit -m "feat(filters): add spec v2 simple aliases to PropertyAccess.Resolve"
```

## Task 2: Add category derivation to PropertyAccess.Resolve

Add `category` as a client-side derived column. Category is extracted from the stream name as text before the
first `-`. If no `-` exists, the full stream name is the category.

**Files:**

- Modify: `src/Kurrent.Client/Modules/Streams/Models/Filters/ExpressionFilter.Parser.cs` (PropertyAccess.Resolve)
- Modify: `test/Kurrent.Client.Tests/Filters/ExpressionFilterTests.cs`

**Step 1: Write failing tests**

Add tests to `ExpressionFilterTests.cs` that verify `category` resolves correctly against records:

- Record with `stream = "order-abc123"` matches `category = 'order'`
- Record with `stream = "payment-2026-001"` matches `category = 'payment'`
- Record with `stream = "nodelimiter"` matches `category = 'nodelimiter'`
- Record with `stream = "order-abc123"` does NOT match `category = 'payment'`

**Step 2: Run tests to verify they fail**

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/ExpressionFilterTests/*"
```

Expected: FAIL because `category` resolves to `null`.

**Step 3: Implement category derivation**

Add to `PropertyAccess.Resolve` switch:

```csharp
"category" => DeriveCategory(record.Stream.Value),
```

Add a static helper method near `PropertyAccess`:

```csharp
static string DeriveCategory(string stream) {
    var idx = stream.IndexOf('-');
    return idx < 0 ? stream : stream[..idx];
}
```

**Step 4: Run tests to verify they pass**

Same command as step 2. Expected: PASS.

**Step 5: Commit**

```sh
git commit -m "feat(filters): add category column with client-side derivation from stream name"
```

## Task 3: Add index virtual column to PropertyAccess.Resolve

Add `index` as a virtual column that always evaluates to `true` client-side. The `index` column is a routing
directive; the server filters by index membership when reading from `$idx-user-*` streams.

**Files:**

- Modify: `src/Kurrent.Client/Modules/Streams/Models/Filters/ExpressionFilter.Parser.cs` (PropertyAccess.Resolve)
- Modify: `test/Kurrent.Client.Tests/Filters/ExpressionFilterTests.cs`

**Step 1: Write failing tests**

Add tests to `ExpressionFilterTests.cs`:

- `index = 'orders-by-country'` matches any record (always true)
- `index = 'orders-by-country' AND schema = 'OrderPlaced'` matches only OrderPlaced records

**Step 2: Run tests to verify they fail**

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/ExpressionFilterTests/*"
```

Expected: FAIL because `index = 'orders-by-country'` evaluates `null = 'orders-by-country'` which is `false`.

**Step 3: Implement index virtual column**

Add to `PropertyAccess.Resolve` switch. The `index` column is special: it needs to match the compared value
regardless of what that value is, because client-side it is always true. The simplest approach is to return a
sentinel that `ComparisonExpression` recognizes.

However, a cleaner approach: return the value being compared against, so `index = 'X'` becomes `'X' = 'X'`
which is always true. This requires access to the comparison value, which `Resolve` does not have.

Simplest correct approach: add `index` to the switch returning a special marker, and handle it in the
`ComparisonExpression.Evaluate` or at the `PropertyAccess` level. The cleanest option is to have `Resolve`
return a `bool` (`true`), and let `ValueComparer` handle `bool = string` as truthy:

```csharp
"index" => true,
```

Then in `ComparisonExpression`, when the resolved property value is `bool` and the comparison is `=`, treat
`true` as matching any non-null value. This needs validation against the existing `ValueComparer` logic.

Alternative: handle `index` at the `IFilterExpression` level. When the parser sees `index = 'X'`, produce a
specialized `IndexExpression` node that always returns `true` from `IsMatch` but carries the index name for
the `ServerFilterExtractor`. This is cleaner because it separates the routing concern from property resolution.

**Recommended: produce a specialized node.** Add an `IndexExpression` that wraps the index name and always
returns `true`. The `ServerFilterExtractor` recognizes `IndexExpression` and extracts the stream name.

```csharp
sealed record IndexExpression(string IndexName) : IFilterExpression {
    public bool IsMatch(Record record) => true;
}
```

In `ParsePropertyAccess` / the comparison parsing path: when the left-hand property name is `index`, produce
an `IndexExpression` instead of a `ComparisonExpression`.

**Step 4: Run tests to verify they pass**

Same command as step 2. Expected: PASS.

**Step 5: Commit**

```sh
git commit -m "feat(filters): add index virtual column with IndexExpression node"
```

## Task 4: Extend ParsePropertyAccess for multi-segment dot paths

Currently `ParsePropertyAccess` handles one dot segment (`properties.key`). Extend it to handle arbitrary
depth (`rec.schema.name`, `rec.position.stream`, `rec.position.streamRevision`).

**Files:**

- Modify: `src/Kurrent.Client/Modules/Streams/Models/Filters/ExpressionFilter.Parser.cs` (ParsePropertyAccess)
- Modify: `test/Kurrent.Client.Tests/Filters/ExpressionFilterParserTests.cs`

**Step 1: Write failing tests**

Add parser tests that verify multi-segment dot paths parse without error:

- `rec.schema.name = 'OrderPlaced'` parses successfully
- `rec.position.stream = 'orders-99'` parses successfully
- `rec.position.streamRevision > 5` parses successfully
- `rec.position.logPosition > 1000` parses successfully
- Existing single-dot tests (`properties.key`) still pass

**Step 2: Run tests to verify they fail**

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/ExpressionFilterParserTests/*"
```

Expected: FAIL with parse error at the second `.` token.

**Step 3: Implement multi-segment dot paths**

Modify `ParsePropertyAccess` to consume additional `.identifier` segments, building the full dotted name:

```csharp
PropertyAccess ParsePropertyAccess() {
    var     identifier = Expect(TokenType.Identifier, "identifier");
    var     name       = identifier.Value;
    string? key        = null;

    if (Peek().Type == TokenType.LeftBracket) {
        Advance();
        var keyToken = Expect(TokenType.String, "string key");
        key = keyToken.Value;
        Expect(TokenType.RightBracket, "]");
    }
    else if (Peek().Type == TokenType.Dot) {
        // consume all dot-separated segments
        var segments = new List<string> { name };
        while (Peek().Type == TokenType.Dot) {
            Advance();
            var segment = Expect(TokenType.Identifier, "property segment");
            segments.Add(segment.Value);
        }

        // for property maps (properties, props, metadata), the last segment is the key
        var baseName = segments[0].ToLowerInvariant();
        if (baseName is "properties" or "props" or "metadata") {
            name = segments[0];
            key  = string.Join(".", segments[1..]);
        } else {
            // join all segments as the full property name (e.g., "rec.schema.name")
            name = string.Join(".", segments);
        }
    }

    return new PropertyAccess(name, key);
}
```

**Step 4: Run tests to verify they pass**

Same command as step 2. Expected: PASS (parsing succeeds, but resolution may return `null` until Task 5).

**Step 5: Commit**

```sh
git commit -m "feat(filters): extend ParsePropertyAccess for multi-segment dot paths"
```

## Task 5: Add rec.* aliases to PropertyAccess.Resolve

With multi-segment dot paths parsing correctly, add the `rec.*` aliases to the `Resolve` switch.

**Files:**

- Modify: `src/Kurrent.Client/Modules/Streams/Models/Filters/ExpressionFilter.Parser.cs` (PropertyAccess.Resolve)
- Modify: `test/Kurrent.Client.Tests/Filters/ExpressionFilterTests.cs`

**Step 1: Write failing tests**

Add tests to `ExpressionFilterTests.cs` verifying `rec.*` aliases resolve correctly against records:

- `rec.stream` resolves to `record.Stream.Value`
- `rec.category` resolves to category derivation
- `rec.schema.name` resolves to `record.Schema.SchemaName.Value`
- `rec.revision` resolves to `record.Revision`
- `rec.position.stream` resolves to `record.Stream.Value`
- `rec.position.streamRevision` resolves to `record.Revision`
- `rec.position.logPosition` resolves to `record.Position.LogPosition.Value`
- `rec.timestamp` resolves to `record.Timestamp`
- `rec.id` resolves to `record.Id`
- `rec.properties` with key resolves to `record.Metadata[key]`

**Step 2: Run tests to verify they fail**

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/ExpressionFilterTests/*"
```

Expected: FAIL because `rec.*` names resolve to `null`.

**Step 3: Implement rec.* aliases**

Add to `PropertyAccess.Resolve` switch:

```csharp
"rec.stream" or "rec.position.stream"             => record.Stream.Value,
"rec.category"                                    => DeriveCategory(record.Stream.Value),
"rec.schema.name"                                 => record.Schema.SchemaName.Value,
"rec.revision" or "rec.position.streamrevision"   => (long)record.Revision,
"rec.position.logposition"                        => record.Position.LogPosition.Value,
"rec.timestamp"                                   => record.Timestamp,
"rec.id"                                          => record.Id,
"rec.properties" => Key is null
    ? null
    : record.Metadata.TryGetValue<object>(Key, out var v)
        ? v
        : null,
```

Note: the switch uses `ToLowerInvariant()` so `rec.position.streamRevision` becomes `rec.position.streamrevision`.

**Step 4: Run tests to verify they pass**

Same command as step 2. Expected: PASS.

**Step 5: Commit**

```sh
git commit -m "feat(filters): add rec.* dot-notation aliases to PropertyAccess.Resolve"
```

## Task 6: Add field_ prefix stripping

When a column name starts with `field_`, strip the prefix and re-resolve. This supports the WebUI convention
where user-defined index fields are prefixed with `field_`.

**Files:**

- Modify: `src/Kurrent.Client/Modules/Streams/Models/Filters/ExpressionFilter.Parser.cs` (PropertyAccess.Resolve)
- Modify: `test/Kurrent.Client.Tests/Filters/ExpressionFilterParserTests.cs`

**Step 1: Write failing tests**

- `field_stream = 'orders-99'` resolves same as `stream = 'orders-99'`
- `field_category = 'orders'` resolves same as `category = 'orders'`
- `field_schema = 'OrderPlaced'` resolves same as `schema = 'OrderPlaced'`

**Step 2: Run tests to verify they fail**

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/ExpressionFilterParserTests/*"
```

Expected: FAIL because `field_stream` resolves to `null`.

**Step 3: Implement field_ prefix stripping**

Replace the catch-all `_ => null` in `PropertyAccess.Resolve` with:

```csharp
_ => PropertyName.StartsWith("field_", StringComparison.OrdinalIgnoreCase)
    ? new PropertyAccess(PropertyName[6..], Key).Resolve(record)
    : null
```

This recursively re-resolves after stripping the prefix. The recursion is bounded (one level) because the
stripped name will not start with `field_` again.

**Step 4: Run tests to verify they pass**

Same command as step 2. Expected: PASS.

**Step 5: Commit**

```sh
git commit -m "feat(filters): add field_ prefix stripping for WebUI compatibility"
```

## Task 7: Add category extraction to ServerFilterExtractor

Extend `ServerFilterExtractor` to recognize `category` predicates and resolve them to
`StreamFilter("$idx-ce-{value}")`. This is priority 2, after stream (priority 1) and before schema (priority 4).

**Files:**

- Modify: `src/Kurrent.Client/Modules/Streams/Models/Filters/ExpressionFilter.ServerExtractor.cs`
- Modify: `test/Kurrent.Client.Tests/Filters/ServerFilterExtractorTests.cs`

**Step 1: Write failing tests**

Add tests to `ServerFilterExtractorTests.cs`:

- `category = 'orders'` extracts `StreamFilter` with `TargetStream = "$idx-ce-orders"`
- `category IN ('orders', 'payments')` extracts `StreamFilter` matching both `$idx-ce-orders` and
  `$idx-ce-payments`
- `category LIKE 'order%'` extracts `StreamFilter` with a regex matching `$idx-ce-order.*`
- `category = 'orders' AND schema = 'OrderPlaced'` extracts only the category `StreamFilter` (category wins
  over schema)
- `stream = 'orders-99' AND category = 'orders'` extracts only the stream `StreamFilter` (stream wins over
  category)
- `category = 'orders' OR schema = 'OrderPlaced'` extracts `RecordFilter.None` (OR not liftable)

**Step 2: Run tests to verify they fail**

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/ServerFilterExtractorTests/*"
```

Expected: FAIL because category predicates are not extracted.

**Step 3: Implement category extraction**

In `ServerFilterExtractor`, add `TryExtractCategoryFilter` following the same pattern as
`TryExtractStreamFilter`. The key difference: when building the regex pattern from a category value, prepend
`$idx-ce-` to the value.

For `category = 'orders'`: build `StreamFilter` targeting `"$idx-ce-orders"` exactly. Use
`new StreamFilter(["$idx-ce-orders"], exactMatch: true)` which gives `TargetStream = "$idx-ce-orders"`.

For `category IN ('orders', 'payments')`: build `StreamFilter` targeting
`["$idx-ce-orders", "$idx-ce-payments"]` with `exactMatch: true`.

For `category LIKE 'order%'`: convert the LIKE pattern to regex, then prepend `$idx-ce-` to the pattern. The
resulting `StreamFilter` regex matches `$idx-ce-order.*`.

Add `IsCategoryProperty` helper: matches `"category"` (case-insensitive).

Insert the category extraction call between stream and schema extraction in the `Extract` method:

```csharp
if (TryExtractStreamFilter(conjuncts, out var streamFilter))
    return streamFilter;

if (TryExtractCategoryFilter(conjuncts, out var categoryFilter))
    return categoryFilter;

if (TryExtractSchemaFilter(conjuncts, out var schemaFilter))
    return schemaFilter;
```

**Step 4: Run tests to verify they pass**

Same command as step 2. Expected: PASS.

**Step 5: Commit**

```sh
git commit -m "feat(filters): add category extraction to ServerFilterExtractor via $idx-ce-* streams"
```

## Task 8: Add index extraction to ServerFilterExtractor

Extend `ServerFilterExtractor` to recognize `IndexExpression` nodes (from Task 3) and resolve them to
`StreamFilter("$idx-user-{name}")`. This is priority 3, after category and before schema.

**Files:**

- Modify: `src/Kurrent.Client/Modules/Streams/Models/Filters/ExpressionFilter.ServerExtractor.cs`
- Modify: `test/Kurrent.Client.Tests/Filters/ServerFilterExtractorTests.cs`

**Step 1: Write failing tests**

Add tests to `ServerFilterExtractorTests.cs`:

- `index = 'orders-by-country'` extracts `StreamFilter` with `TargetStream = "$idx-user-orders-by-country"`
- `index = 'orders-by-country' AND schema = 'OrderPlaced'` extracts only the index `StreamFilter`
- `category = 'orders' AND index = 'orders-by-country'` extracts only the category `StreamFilter` (category
  wins over index)

**Step 2: Run tests to verify they fail**

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/ServerFilterExtractorTests/*"
```

Expected: FAIL because `IndexExpression` is not handled.

**Step 3: Implement index extraction**

In `ServerFilterExtractor`, add `TryExtractIndexFilter`. Since Task 3 introduced `IndexExpression` as a
distinct AST node, the extractor scans the AND conjuncts for `IndexExpression` instances:

```csharp
static bool TryExtractIndexFilter(
    IReadOnlyList<IFilterExpression> conjuncts,
    [NotNullWhen(true)] out IRecordFilter? filter
) {
    foreach (var conjunct in conjuncts) {
        if (conjunct is IndexExpression { IndexName: var name }) {
            filter = new StreamFilter([$"$idx-user-{name}"], exactMatch: true);
            return true;
        }
    }

    filter = null;
    return false;
}
```

Insert between category and schema in `Extract`:

```csharp
if (TryExtractCategoryFilter(conjuncts, out var categoryFilter))
    return categoryFilter;

if (TryExtractIndexFilter(conjuncts, out var indexFilter))
    return indexFilter;

if (TryExtractSchemaFilter(conjuncts, out var schemaFilter))
    return schemaFilter;
```

**Step 4: Run tests to verify they pass**

Same command as step 2. Expected: PASS.

**Step 5: Commit**

```sh
git commit -m "feat(filters): add index extraction to ServerFilterExtractor via $idx-user-* streams"
```

## Task 9: Update ByCategory to target $idx-ce-* streams

Change `RecordFilter.ByCategory` from prefix matching on stream names to exact targeting of `$idx-ce-*`
streams.

**Files:**

- Modify: `src/Kurrent.Client/Modules/Streams/Models/Filters/RecordFilter.cs`
- Modify: `test/Kurrent.Client.Tests/Filters/RecordFilterTests.cs`

**Step 1: Update tests**

Modify the existing `ByCategory` test in `RecordFilterTests.cs` to verify the new behavior:

- `RecordFilter.ByCategory("orders")` produces a `StreamFilter` with `TargetStream = "$idx-ce-orders"`
- `RecordFilter.ByCategory("orders", "payments")` produces a `StreamFilter` matching `$idx-ce-orders` and
  `$idx-ce-payments`

**Step 2: Run tests to verify they fail**

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/RecordFilterTests/*"
```

Expected: FAIL because `ByCategory` still uses prefix matching.

**Step 3: Implement new ByCategory**

Change `RecordFilter.ByCategory` to map category names to `$idx-ce-*` stream names and delegate to
`ByStream`:

```csharp
public static IRecordFilter ByCategory(params string[] categories) {
    ArgumentOutOfRangeException.ThrowIfZero(categories.Length, nameof(categories));
    var streams = categories.Select(c => $"$idx-ce-{c}").ToArray();
    return new StreamFilter(streams, exactMatch: true);
}
```

**Step 4: Run tests to verify they pass**

Same command as step 2. Expected: PASS.

**Step 5: Commit**

```sh
git commit -m "feat(filters): change ByCategory to target $idx-ce-* secondary index streams"
```

## Task 10: Remove prefix matching support

Remove `ByStreamPrefix`, `BySchemaPrefix`, and the `exactMatch` parameter from `RegexFilter`, `StreamFilter`,
and `SchemaFilter`. All regex patterns now use exact matching (trailing `$`).

**Files:**

- Modify: `src/Kurrent.Client/Modules/Streams/Models/Filters/RecordFilter.cs`
- Modify: `src/Kurrent.Client/Modules/Streams/Models/Filters/RegexFilter.cs`
- Modify: `src/Kurrent.Client/Modules/Streams/Models/Filters/StreamFilter.cs`
- Modify: `src/Kurrent.Client/Modules/Streams/Models/Filters/SchemaFilter.cs`
- Modify: `test/Kurrent.Client.Tests/Filters/StreamFilterTests.cs`
- Modify: `test/Kurrent.Client.Tests/Filters/SchemaFilterTests.cs`
- Modify: `test/Kurrent.Client.Tests/Filters/RecordFilterTests.cs`
- Modify: `test/Kurrent.Client.Tests/Filters/ServerFilterExtractorTests.cs`

**Step 1: Remove test usages of ByStreamPrefix and BySchemaPrefix**

In `StreamFilterTests.cs`: remove or rewrite the `#region ByStreamPrefix` tests. If prefix behavior is still
needed for test scenarios, rewrite using `RecordFilter.ByStreamPattern("^Orders-")`.

In `SchemaFilterTests.cs`: remove or rewrite tests using `BySchemaPrefix`.

In `RecordFilterTests.cs`: remove `ByStreamPrefix` and `BySchemaPrefix` tests.

In `ServerFilterExtractorTests.cs`: update any test that references `RecordFilter.ByStreamPrefix` (line 432)
to use the new API.

**Step 2: Remove factory methods and simplify constructors**

In `RecordFilter.cs`:

- Remove `ByStreamPrefix` method
- Remove `BySchemaPrefix` method

In `RegexFilter.cs`:

- Remove `exactMatch` parameter from `RegexFilter(string[] values, bool exactMatch)`
- Change to `RegexFilter(string[] values)` and always append `$` to the pattern

In `StreamFilter.cs`:

- Update constructor to `StreamFilter(string[] streams)` calling `base(streams)`
- Keep TargetStream logic: `if (streams.Length == 1) TargetStream = streams[0];`

In `SchemaFilter.cs`:

- Update constructor to `SchemaFilter(string[] schemas)` calling `base(schemas)`

**Step 3: Fix all compilation errors**

Update all remaining call sites that pass `exactMatch`:

- `RecordFilter.ByStream` calls `new StreamFilter(streams, exactMatch: true)` → `new StreamFilter(streams)`
- `RecordFilter.BySchema` calls `new SchemaFilter(schemas, exactMatch: true)` → `new SchemaFilter(schemas)`
- `RecordFilter.ByCategory` (updated in Task 9) calls with `exactMatch: true` → remove parameter

**Step 4: Build and run all filter tests**

```sh
dotnet build src/Kurrent.Client/
```

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/StreamFilterTests/*"
```

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/SchemaFilterTests/*"
```

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/RecordFilterTests/*"
```

```sh
dotnet run --project test/Kurrent.Client.Tests/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --adaptive-metrics --output Detailed --log-level Trace \
  --framework net8.0 \
  --treenode-filter "/*/*/ServerFilterExtractorTests/*"
```

Expected: all PASS, zero compilation errors.

**Step 5: Commit**

```sh
git commit -m "refactor(filters): remove prefix matching in favor of direct $idx-* stream targeting"
```
