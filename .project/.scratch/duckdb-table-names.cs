#:package DuckDB.NET.Data.Full@*
#:property PublishAot=false

using DuckDB.NET.Data;

var tableNames = new[] {
    "db.records",
    "idx_main",
    "idx-log",
    "$idx-user-orders-by-country",
    "$idx-user-orders-by-country:portugal",
};

using var connection = new DuckDBConnection("DataSource=:memory:");
connection.Open();

foreach (var name in tableNames) {
    var quoted = $"\"{name}\"";
    try {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"CREATE TABLE {quoted} (id INTEGER, value TEXT)";
        cmd.ExecuteNonQuery();

        using var verify = connection.CreateCommand();
        verify.CommandText = $"SELECT COUNT(*) FROM {quoted}";
        var count = verify.ExecuteScalar();

        Console.WriteLine($"OK    {name,-50} (created, queried)");
    } catch (Exception ex) {
        Console.WriteLine($"FAIL  {name,-50} {ex.Message}");
    }
}
