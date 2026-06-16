using System.Diagnostics.CodeAnalysis;
using DuckDB.NET.Data;
using DuckDB.NET.Native;
using static System.StringComparison;

namespace Kurrent.Projectors.DuckDB;

[PublicAPI]
public sealed record DuckDBConnectionString(
    string Value,
    string DataSource,
    bool InMemory,
    bool Shared,
    bool IsReadOnly,
    IReadOnlyDictionary<string, string> Configuration
) {
    static readonly HashSet<string> ConfigurationOptions = new(StringComparer.OrdinalIgnoreCase);

    static DuckDBConnectionString() {
        var configCount = NativeMethods.Configuration.DuckDBConfigCount();

        for (var index = 0; index < configCount; index++) {
            NativeMethods.Configuration.DuckDBGetConfigFlag(index, out var name, out _);
            ConfigurationOptions.Add(name);
        }
    }

    const string DataSourceKey = "DataSource";

    public static DuckDBConnectionString Parse(string connectionString) {
        var config = connectionString
            .Replace("Data Source", DataSourceKey, OrdinalIgnoreCase)
            .Split(';')
            .Select(x => x.Split('='))
            .ToDictionary(entry => entry[0], entry => entry.Length > 1 ? entry[1] : "", StringComparer.OrdinalIgnoreCase);

        if (!config.TryGetValue(DataSourceKey, out var dataSource) || string.IsNullOrEmpty(dataSource))
            throw new ArgumentException($"Connection string '{connectionString}' is not valid, missing data source information.");

        config.Remove(DataSourceKey);

        var invalidOptions = config
            .Keys
            .Where(x => !ConfigurationOptions.Contains(x))
            .ToArray();

        if (invalidOptions.Length > 0)
            throw new ArgumentException($"Connection string '{connectionString}' contains invalid configuration options '{string.Join(", ", invalidOptions)}'.");

        var isInMemoryShared = dataSource
            .Equals(DuckDBConnectionStringBuilder.InMemorySharedDataSource, OrdinalIgnoreCase);

        var inMemory = isInMemoryShared || dataSource
            .Equals(DuckDBConnectionStringBuilder.InMemoryDataSource, OrdinalIgnoreCase);

        var isReadonly = config.TryGetValue("access_mode", out var value) && value.Equals("read_only", OrdinalIgnoreCase);

        return new(
            connectionString, dataSource, inMemory,
            isInMemoryShared, isReadonly, config);
    }

    public static bool TryParse(string connectionString, [NotNullWhen(true)] out DuckDBConnectionString? result, [NotNullWhen(false)] out Exception? exception) {
        try {
            result    = Parse(connectionString);
            exception = null;
            return true;
        }
        catch (Exception ex) {
            result    = null;
            exception = ex;
            return false;
        }
    }

    public override string ToString() => Value;

    public static implicit operator DuckDBConnectionString(string value) =>
        Parse(value);

    public static implicit operator string(DuckDBConnectionString connString) =>
        connString.Value;

    public static implicit operator DuckDBConnection(DuckDBConnectionString connString) =>
        new(connString.Value);
}