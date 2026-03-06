#:package DuckDB.NET.Data.Full@*
#:property PublishAot=false

using DuckDB.NET.Data;

using var connection = new DuckDBConnection("DataSource=:memory:");
connection.Open();

// ── System schema: "db" holds internal tables ──

Exec("CREATE SCHEMA db");
Exec("CREATE TABLE db.records (id INTEGER, stream TEXT, data TEXT)");

// ── User indexes: plain table names in default (main) schema ──

var userIndexes = new[] {
    "orders-by-country",
    "orders-by-region",
    "users-by-email",
};

foreach (var idx in userIndexes)
    Exec($"CREATE TABLE \"{idx}\" (id INTEGER, value TEXT)");

// ── Seed some data ──

Exec("INSERT INTO db.records VALUES (1, 'order-abc', 'payload-1'), (2, 'order-xyz', 'payload-2')");
Exec("INSERT INTO \"orders-by-country\" VALUES (1, 'portugal'), (2, 'spain')");

// ── Test 1: Query system table with schema prefix ──

Section("Query db.records (schema-qualified)");
Query("SELECT * FROM db.records");

// ── Test 2: Query user index by plain name ──

Section("Query user index (unqualified)");
Query("SELECT * FROM \"orders-by-country\"");

// ── Test 3: No collision — "records" in default schema is independent ──

Section("No collision: main.records vs db.records");
Exec("CREATE TABLE records (id INTEGER, note TEXT)");
Exec("INSERT INTO records VALUES (99, 'main-schema')");
Query("SELECT 'main' as schema, * FROM records");
Query("SELECT 'db' as schema, * FROM db.records");

// ── Test 4: List all tables showing schema separation ──

Section("All tables (information_schema)");
Query("SELECT table_schema, table_name FROM information_schema.tables WHERE table_schema NOT IN ('information_schema', 'pg_catalog') ORDER BY table_schema, table_name");

// ── Test 5: Can user accidentally shadow a system table? ──

Section("User cannot create tables in db schema without explicit db. prefix");
try {
    Exec("SET schema = 'db'");
    Exec("CREATE TABLE user_sneaky (id INTEGER)");
    Query("SELECT table_schema, table_name FROM information_schema.tables WHERE table_name = 'user_sneaky'");
    Console.WriteLine("  ^ If schema=db, user table lands in db schema (needs access control or convention)");
} catch (Exception ex) {
    Console.WriteLine($"  BLOCKED: {ex.Message}");
}

// ── Test 6: search_path controls resolution order ──

Section("search_path resolution: main first, then db");
Exec("SET search_path = 'main, db'");
Query("SELECT 'resolved via search_path' as note, * FROM records LIMIT 1");
// This should find main.records (main is first in search_path)

// ── Test 7: Qualified FROM for the expression filter mapping ──

Section("Expression filter mapping");
Console.WriteLine("  FROM db.records          -> system records table");
Console.WriteLine("  FROM \"orders-by-country\"  -> user index (main schema)");
Console.WriteLine("  FROM orders_by_country   -> user index (if unquoted-safe name)");

// ── Helpers ──

void Exec(string sql) {
    using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    try {
        cmd.ExecuteNonQuery();
    } catch (Exception ex) {
        Console.WriteLine($"  EXEC FAIL [{sql}]: {ex.Message}");
    }
}

void Query(string sql) {
    using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    try {
        using var reader = cmd.ExecuteReader();
        var cols = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        Console.WriteLine($"  {string.Join(" | ", cols)}");
        Console.WriteLine($"  {new string('-', cols.Sum(c => c.Length + 3))}");
        while (reader.Read()) {
            var vals = Enumerable.Range(0, reader.FieldCount).Select(i => reader[i]?.ToString() ?? "NULL");
            Console.WriteLine($"  {string.Join(" | ", vals)}");
        }
    } catch (Exception ex) {
        Console.WriteLine($"  QUERY FAIL [{sql}]: {ex.Message}");
    }
}

void Section(string title) {
    Console.WriteLine();
    Console.WriteLine($"── {title} ──");
}
