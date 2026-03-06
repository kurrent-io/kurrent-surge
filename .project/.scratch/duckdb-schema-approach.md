# DuckDB Schema Approach for Stream Name Resolution

## Problem

Current design uses `$idx-*` stream prefixes to distinguish system streams from user indexes. This requires
special parsing logic and produces names that need quoting everywhere (`$idx-ce-order`,
`$idx-user-orders-by-country`).

## Proposal

Use DuckDB schemas to separate system tables from user indexes:

- **`db` schema** holds internal/system tables (e.g. `db.records`)
- **`main` schema** (default) holds user-created indexes as plain table names

```sql
-- system table: schema-qualified, no quoting needed
SELECT * FROM db.records

-- user index: plain table name in default schema
SELECT * FROM "orders-by-country"
```

## Tested Mappings

```
FROM db.records              -> system records table (db schema)
FROM "orders-by-country"     -> user index (main schema)
FROM orders_by_region        -> user index (main schema, no quoting if no hyphens)
```

Replaces:

```
FROM category:order          -> StreamFilter.Prefix("$idx-ce-order")
FROM index:orders-by-country -> StreamFilter.Prefix("$idx-user-orders-by-country")
```

## Validation Results

| Test                              | Result  | Notes                                                      |
| --------------------------------- | ------- | ---------------------------------------------------------- |
| `db.records` (schema-qualified)   | OK      | No quoting needed for dotted schema.table syntax            |
| User indexes as plain table names | OK      | Hyphenated names require quoting; underscored names do not |
| `main.records` vs `db.records`    | OK      | Same table name in different schemas; fully independent    |
| Schema listing via information_schema | OK  | Clean separation visible in metadata                       |
| `SET schema='db'` leakage         | Caution | Tables land in `db` schema; convention-enforced, not ACL   |

## Caveats

- DuckDB has no schema-level access control. Isolation is by naming convention only.
- Hyphenated table names (`orders-by-country`) still require double-quoting in SQL.
- `search_path` multi-schema resolution works but is sensitive to whitespace in the value string.

## Test Script

See `.project/.scratch/duckdb-schema-test.cs` for the full validation script.
