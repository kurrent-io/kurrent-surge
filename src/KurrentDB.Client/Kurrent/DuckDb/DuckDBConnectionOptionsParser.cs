using System.Text;
using System.Web;
using Microsoft.Extensions.Logging;
using static System.StringComparison;

namespace Kurrent.DuckDB;

/// <summary>
/// Thrown when a DuckDB connection string is invalid.
/// </summary>
public sealed class InvalidDuckDbConnectionStringException(string message) : Exception(message);

/// <summary>
/// Parses and serializes DuckDB connection options to/from connection strings.
/// </summary>
static class DuckDBConnectionOptionsParser {
    const char   Separator     = ';';
    const char   KeyValueSep   = '=';
    const string DataSourceKey = "DataSource";

    internal const string InMemoryDataSource       = ":memory:";
    internal const string InMemorySharedDataSource = ":memory:?cache=shared";
    internal const string MotherDuckPrefix         = "md:";
    internal const string DuckLakePrefix           = "ducklake:";

    internal const string KeyAccessMode    = "access_mode";
    internal const string KeyThreads       = "threads";
    internal const string KeyMaxMemory     = "max_memory";
    internal const string KeyEnableLogging = "enable_logging";
    internal const string KeyLoggingLevel  = "logging_level";

    static readonly Dictionary<string, string> KeyAliases = new(StringComparer.OrdinalIgnoreCase) {
        ["worker_threads"] = KeyThreads,
        ["memory_limit"] = KeyMaxMemory,
    };

    /// <summary>
    /// Parses a connection string into connection options.
    /// </summary>
    /// <exception cref="InvalidDuckDbConnectionStringException">Thrown when the connection string is invalid.</exception>
    public static DuckDBConnectionOptions Parse(string connectionString, bool strict = false) {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidDuckDbConnectionStringException("Connection string cannot be null or empty.");

        string? dataSource = null;
        var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in connectionString.Split(Separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
            var eqIndex = segment.IndexOf(KeyValueSep);
            if (eqIndex <= 0)
                throw new InvalidDuckDbConnectionStringException($"Invalid key-value pair: '{segment}'. Expected format: key=value");

            var key   = segment[..eqIndex].Trim();
            var value = segment[(eqIndex + 1)..].Trim();

            if (IsDataSourceKey(key)) {
                if (dataSource is not null)
                    throw new InvalidDuckDbConnectionStringException("Connection string contains multiple DataSource entries.");
                dataSource = value;
            }
            else {
                config[NormalizeKey(key)] = value;
            }
        }

        if (dataSource is null)
            throw new InvalidDuckDbConnectionStringException("Connection string must contain a DataSource entry.");

        if (strict)
            ValidateKeys(config.Keys);

        return Build(dataSource, config);
    }

    /// <summary>
    /// Converts connection options to a connection string.
    /// </summary>
    public static string ToConnectionString(DuckDBConnectionOptions options) {
        var sb = new StringBuilder();
        sb.Append(DataSourceKey).Append(KeyValueSep).Append(options.DataSource);

        foreach (var (key, value) in options.Configuration)
            sb.Append(Separator).Append(key).Append(KeyValueSep).Append(value);

        return sb.ToString();
    }

    internal static string NormalizeKey(string key) =>
        KeyAliases.GetValueOrDefault(key, key);

    internal static string FormatMaxMemory(long megabytes) {
        return megabytes switch {
            >= 1024 * 1024 when megabytes % (1024 * 1024) == 0 => $"{megabytes / (1024 * 1024)}TB",
            >= 1024 when megabytes % 1024 == 0                 => $"{megabytes / 1024}GB",
            _                                                  => $"{megabytes}MB"
        };
    }

    static DataSourceType DetectType(string dataSource) {
        if (IsMotherDuck(dataSource)) return DataSourceType.MotherDuck;
        if (IsDuckLake(dataSource))   return DataSourceType.DuckLake;
        if (IsInMemory(dataSource))   return DataSourceType.InMemory;
        return DataSourceType.File;
    }

    static bool IsInMemory(string dataSource) =>
        dataSource.StartsWith(InMemoryDataSource, OrdinalIgnoreCase);

    static bool IsMotherDuck(string dataSource) =>
        dataSource.StartsWith(MotherDuckPrefix, OrdinalIgnoreCase);

    static bool IsDuckLake(string dataSource) =>
        dataSource.StartsWith(DuckLakePrefix, OrdinalIgnoreCase);

    static MotherDuckOptions ParseMotherDuckOptions(string dataSource) {
        if (!IsMotherDuck(dataSource))
            return MotherDuckOptions.Default;

        var database = ParseMotherDuckDatabase(dataSource);
        var token    = ParseMotherDuckToken(dataSource);

        return new MotherDuckOptions { Database = database, Token = token };
    }

    static string ParseMotherDuckDatabase(string dataSource) {
        var withoutPrefix = dataSource.AsSpan()[MotherDuckPrefix.Length..];
        var queryIndex    = withoutPrefix.IndexOf('?');

        return queryIndex >= 0
            ? withoutPrefix[..queryIndex].ToString()
            : withoutPrefix.ToString();
    }

    static string ParseMotherDuckToken(string dataSource) {
        var queryIndex = dataSource.IndexOf('?');
        if (queryIndex < 0)
            return "";

        var query  = dataSource[(queryIndex + 1)..];
        var parsed = HttpUtility.ParseQueryString(query);
        return parsed["motherduck_token"] ?? "";
    }

    static DuckLakeOptions ParseDuckLakeOptions(string dataSource) {
        if (!IsDuckLake(dataSource))
            return DuckLakeOptions.Default;

        var metadataPath = dataSource[DuckLakePrefix.Length..];
        return new DuckLakeOptions { MetadataPath = metadataPath };
    }

    static bool IsDataSourceKey(string key) =>
        key.Equals(DataSourceKey, OrdinalIgnoreCase) ||
        key.Equals("Data Source", OrdinalIgnoreCase);

    static void ValidateKeys(IEnumerable<string> keys) {
        var invalidKeys = DuckDBNativeConfiguration.GetInvalidKeys(keys);
        if (invalidKeys.Count == 0)
            return;

        var invalidKey = invalidKeys[0];
        var suggestion = DuckDBNativeConfiguration.SuggestSimilar(invalidKey);

        throw new InvalidDuckDbConnectionStringException(
            suggestion is not null
                ? $"Unknown configuration key: '{invalidKey}'. Did you mean '{suggestion}'?"
                : $"Unknown configuration key: '{invalidKey}'."
        );
    }

    static DuckDBConnectionOptions Build(string dataSource, Dictionary<string, string> config) {
        var type           = DetectType(dataSource);
        var loggingEnabled = ParseEnableLogging(config.GetValueOrDefault(KeyEnableLogging));
        var loggingLevel   = ParseLoggingLevel(config.GetValueOrDefault(KeyLoggingLevel));

        return new DuckDBConnectionOptions {
            Type          = type,
            DataSource    = dataSource,
            Configuration = config,
            IsReadOnly    = ParseIsReadOnly(config.GetValueOrDefault(KeyAccessMode)),
            Threads       = ParseThreads(config.GetValueOrDefault(KeyThreads)),
            MaxMemoryMB   = ParseMaxMemory(config.GetValueOrDefault(KeyMaxMemory)) ?? 0,
            Logging       = loggingEnabled ? new DuckDBLoggingOptions { Enabled = true, Level = loggingLevel } : DuckDBLoggingOptions.Default,
            MotherDuck    = ParseMotherDuckOptions(dataSource),
            DuckLake      = ParseDuckLakeOptions(dataSource)
        };
    }

    static bool ParseIsReadOnly(string? value) =>
        value?.ToUpperInvariant() is "READ_ONLY" or "READONLY";

    static int ParseThreads(string? value) =>
        int.TryParse(value, out var result) && result > 0 ? result : 0;

    internal static long? ParseMaxMemory(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var span = value.AsSpan().Trim();
        if (span.IsEmpty)
            return null;

        var unitStart = 0;
        for (var i = 0; i < span.Length; i++) {
            if (!char.IsDigit(span[i]) && span[i] != '.') {
                unitStart = i;
                break;
            }

            if (i == span.Length - 1)
                unitStart = span.Length;
        }

        if (unitStart == 0)
            return null;

        var numberPart = span[..unitStart];
        var unitPart   = span[unitStart..].Trim();

        if (!double.TryParse(numberPart, out var number) || number < 0)
            return null;

        var multiplier = unitPart.ToString().ToUpperInvariant() switch {
            "B" or ""   => 1.0 / (1024 * 1024),
            "KB" or "K" => 1.0 / 1024,
            "MB" or "M" => 1.0,
            "GB" or "G" => 1024.0,
            "TB" or "T" => 1024.0 * 1024,
            _           => -1.0
        };

        if (multiplier < 0)
            return null;

        return (long)(number * multiplier);
    }

    static bool ParseEnableLogging(string? value) =>
        value?.ToLowerInvariant() is "true" or "1";

    static LogLevel ParseLoggingLevel(string? value) => value?.ToUpperInvariant() switch {
        "CRITICAL" => LogLevel.Critical,
        "ERROR"    => LogLevel.Error,
        "WARN"     => LogLevel.Warning,
        "INFO"     => LogLevel.Information,
        "DEBUG"    => LogLevel.Debug,
        "TRACE"    => LogLevel.Trace,
        _          => LogLevel.Warning
    };
}
