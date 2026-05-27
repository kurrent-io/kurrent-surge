using Apache.Arrow.Adbc.Client;
using Dapper;

namespace Kurrent;

public record RecordDto {
    public string Id          { get; init; }
    public string LogPosition { get; init; }
    public string Stream      { get; set; }
    public object Value       { get; set; }
    public byte[] Data        { get; set; }
}

public sealed class Queries : IDisposable {
    readonly Apache.Arrow.Adbc.Drivers.FlightSql.FlightSqlDriver _driver;
    readonly string                                             _connectionString;

    public Queries(string connectionString) {
        _connectionString = connectionString;
        _driver           = new();
    }

    public IAsyncEnumerable<RecordDto> Query(string sql) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        
        if (!sql.Contains("from kdb.records", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Query must target 'kdb.records'", nameof(sql));
        
        return Yield();
        
        async IAsyncEnumerable<RecordDto> Yield() {
            await using var connection = _driver.CreateConnection(_connectionString);
            await foreach(var record in connection.QueryUnbufferedAsync<RecordDto>(sql))
                yield return record;
        }
    }
    
    public void Dispose() =>
        _driver.Dispose();
}