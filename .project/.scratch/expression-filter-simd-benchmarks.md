# ExpressionFilter SIMD Performance Optimization Results

Branch: `perf/expression-filter-simd`
Date: 2026-03-09
Runtime: net10.0 (ARM64)

## Contents

- [Parse benchmarks](#parse-benchmarks)
- [Eval benchmarks](#eval-benchmarks)
- [Parse comparison](#parse-comparison)
- [Eval comparison](#eval-comparison)
- [Changes summary](#changes-summary)

## Parse benchmarks

### Baseline (before optimization)

| Expression                                   |     Mean |  Allocated |
| -------------------------------------------- | -------: | ---------: |
| `stream = 'orders-123'`                      |  157 ns  |      472 B |
| `position > 100`                             |  165 ns  |      456 B |
| `metadata['region'] = 'us-east'`             |  251 ns  |      680 B |
| `position BETWEEN 10 AND 100`                |  284 ns  |      776 B |
| `metadata['tenant'] IS NOT NULL`             |  313 ns  |      704 B |
| `stream = '...' OR stream = '...'`           |  340 ns  |      928 B |
| `stream IN ('orders-1', ..., 'orders-3')`    |  405 ns  |    1,200 B |
| `stream = '...' AND ... AND schema = '...'`  |  548 ns  |    1,488 B |
| `stream LIKE 'orders-%'`                     | 3,317 ns |    8,776 B |
| `schema ILIKE '%user%'`                      | 4,402 ns |   10,376 B |

### After optimization

| Expression                                   |   Mean |  Allocated |
| -------------------------------------------- | -----: | ---------: |
| `stream = 'orders-123'`                      | 163 ns |      472 B |
| `position > 100`                             | 170 ns |      456 B |
| `stream LIKE 'orders-%'`                     | 198 ns |      544 B |
| `schema ILIKE '%user%'`                      | 204 ns |      528 B |
| `metadata['region'] = 'us-east'`             | 264 ns |      680 B |
| `position BETWEEN 10 AND 100`                | 288 ns |      776 B |
| `metadata['tenant'] IS NOT NULL`             | 333 ns |      704 B |
| `stream = '...' OR stream = '...'`           | 347 ns |      928 B |
| `stream IN ('orders-1', ..., 'orders-3')`    | 503 ns |    1,536 B |
| `stream = '...' AND ... AND schema = '...'`  | 542 ns |    1,488 B |

## Eval benchmarks

### Baseline (before optimization)

| Scenario                |  Mean |
| ----------------------- | ----: |
| `cat-hit`               | 11 ns |
| `between-hit`           | 13 ns |
| `gt-hit`                | 15 ns |
| `gt-miss`               | 16 ns |
| `eq-miss`               | 21 ns |
| `eq-hit`                | 22 ns |
| `and3-miss@1`           | 23 ns |
| `or2-hit@1`             | 23 ns |
| `like-miss`             | 23 ns |
| `wide-and5-miss@1`      | 25 ns |
| `like-hit`              | 26 ns |
| `complex-nest-hit`      | 26 ns |
| `ilike-hit`             | 28 ns |
| `mixed-in-btwn-like-miss` | 28 ns |
| `notnull-hit`           | 31 ns |
| `in-hit`                | 31 ns |
| `complex-nest-miss`     | 34 ns |
| `in-miss`               | 39 ns |
| `meta-hit`              | 41 ns |
| `or2-miss`              | 45 ns |
| `and3-hit`              | 61 ns |
| `mixed-in-btwn-like-hit` | 74 ns |
| `wide-and5-hit`         | 89 ns |

### After optimization

| Scenario                |  Mean |
| ----------------------- | ----: |
| `cat-hit`               | 11 ns |
| `like-matchall`         | 12 ns |
| `between-hit`           | 13 ns |
| `like-miss`             | 14 ns |
| `gt-hit`                | 15 ns |
| `like-hit`              | 15 ns |
| `like-exact-miss`       | 15 ns |
| `like-suffix-miss`      | 15 ns |
| `gt-miss`               | 15 ns |
| `like-suffix-hit`       | 16 ns |
| `like-exact-hit`        | 16 ns |
| `in-large-miss` (8)     | 17 ns |
| `in-hit`                | 19 ns |
| `mixed-in-btwn-like-miss` | 19 ns |
| `eq-miss`               | 21 ns |
| `ilike-hit`             | 21 ns |
| `eq-hit`                | 21 ns |
| `in-large-hit` (8)      | 21 ns |
| `in-miss`               | 22 ns |
| `or2-hit@1`             | 22 ns |
| `and3-miss@1`           | 24 ns |
| `like-complex-hit`      | 27 ns |
| `complex-nest-hit`      | 27 ns |
| `wide-and5-miss@1`      | 26 ns |
| `notnull-hit`           | 29 ns |
| `complex-nest-miss`     | 30 ns |
| `meta-hit`              | 38 ns |
| `or2-miss`              | 44 ns |
| `mixed-in-btwn-like-hit` | 47 ns |
| `and3-hit`              | 65 ns |
| `wide-and5-hit`         | 90 ns |

All eval scenarios: zero allocation.

## Parse comparison

| Expression                | Before   | After  | Delta  | Alloc Before | Alloc After |
| ------------------------- | -------: | -----: | -----: | -----------: | ----------: |
| `stream LIKE 'orders-%'`  | 3,317 ns | 198 ns | **-94%** |    8,776 B |       544 B |
| `schema ILIKE '%user%'`   | 4,402 ns | 204 ns | **-95%** |   10,376 B |       528 B |
| `stream IN (3 values)`    |   405 ns | 503 ns |   +24% |    1,200 B |     1,536 B |

LIKE/ILIKE: 16-21x faster parse, 93-95% allocation reduction. Regex compilation eliminated for simple patterns.
IN: small parse regression due to FrozenSet construction; pays off at eval time.

## Eval comparison

| Scenario                  | Before | After  | Delta  |
| ------------------------- | -----: | -----: | -----: |
| `like-hit`                |  26 ns |  15 ns | **-41%** |
| `like-miss`               |  23 ns |  14 ns | **-40%** |
| `ilike-hit`               |  28 ns |  21 ns | **-25%** |
| `in-hit` (3 values)       |  31 ns |  19 ns | **-41%** |
| `in-miss` (3 values)      |  39 ns |  22 ns | **-44%** |
| `in-large-hit` (8 values) |    n/a |  21 ns |     n/a |
| `in-large-miss` (8 values)|    n/a |  17 ns |     n/a |
| `mixed-in-btwn-like-hit`  |  74 ns |  47 ns | **-35%** |
| `mixed-in-btwn-like-miss` |  28 ns |  19 ns | **-32%** |

## Changes summary

| Commit    | Description                                            |
| --------- | ------------------------------------------------------ |
| `19f8276` | FrozenDictionary keyword lookup (O(1) vs 11 sequential)|
| `55e2e82` | SearchValues SIMD scanning (NET9+)                     |
| `12e7939` | LIKE fast-path pattern matching (string ops vs Regex)  |
| `a5e67e6` | FrozenSet IN membership (O(1) vs O(n) linear)          |
| `b40f87c` | Dead code cleanup + benchmark scenarios                |
