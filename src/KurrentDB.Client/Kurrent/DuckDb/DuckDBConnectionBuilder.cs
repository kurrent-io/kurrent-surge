using Microsoft.Extensions.Logging;

namespace Kurrent.DuckDB;

/// <summary>
/// Builds <see cref="DuckDBConnectionOptions"/> instances using a fluent API.
/// Use <see cref="DuckDBConnection"/> factory methods to create builders.
/// </summary>
public sealed class DuckDBConnectionBuilder {
    static readonly string[] AllowedFileExtensions = [".ddb", ".db", ".quack", ".duckdb"];

    DuckDBConnectionOptions _options = new();
    readonly Dictionary<string, string> _configuration = new(StringComparer.OrdinalIgnoreCase);

    internal DuckDBConnectionBuilder ConfigureInMemory(bool shared) {
        _options = _options with {
            Type = DataSourceType.InMemory,
            DataSource = shared
                ? DuckDBConnectionOptionsParser.InMemorySharedDataSource
                : DuckDBConnectionOptionsParser.InMemoryDataSource
        };
        return this;
    }

    internal DuckDBConnectionBuilder ConfigureFile(string path) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateDatabasePath(path);

        _options = _options with {
            Type = DataSourceType.File,
            DataSource = path
        };
        return this;
    }

    static void ValidateDatabasePath(string path) {
        var extension = Path.GetExtension(path);

        if (string.IsNullOrEmpty(extension))
            throw new ArgumentException(
                $"Database path must have a file extension. Allowed: {string.Join(", ", AllowedFileExtensions)}",
                nameof(path)
            );

        if (!AllowedFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Invalid database file extension '{extension}'. Allowed: {string.Join(", ", AllowedFileExtensions)}",
                nameof(path)
            );
    }

    internal DuckDBConnectionBuilder ConfigureMotherDuck(string database, string? token) {
        _options = _options with {
            Type = DataSourceType.MotherDuck,
            DataSource = string.IsNullOrEmpty(token)
                ? $"{DuckDBConnectionOptionsParser.MotherDuckPrefix}{database}"
                : $"{DuckDBConnectionOptionsParser.MotherDuckPrefix}{database}?motherduck_token={token}",
            MotherDuck = new MotherDuckOptions { Database = database, Token = token ?? "" }
        };
        return this;
    }

    internal DuckDBConnectionBuilder ConfigureDuckLake(DuckLakeOptions options) {
        _options = _options with {
            Type = DataSourceType.DuckLake,
            DataSource = $"{DuckDBConnectionOptionsParser.DuckLakePrefix}{options.MetadataPath}",
            DuckLake = options
        };
        return this;
    }

    internal DuckDBConnectionBuilder ConfigureFrom(DuckDBConnectionOptions options) {
        _options = options;
        foreach (var (key, value) in options.Configuration)
            _configuration[key] = value;
        return this;
    }

    /// <summary>
    /// Sets read-only access mode.
    /// </summary>
    public DuckDBConnectionBuilder AsReadOnly() {
        _options = _options with { IsReadOnly = true };
        _configuration[DuckDBConnectionOptionsParser.KeyAccessMode] = "READ_ONLY";
        return this;
    }

    /// <summary>
    /// Sets the number of threads.
    /// </summary>
    public DuckDBConnectionBuilder WithThreads(int threads) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threads);
        _options = _options with { Threads = threads };
        _configuration[DuckDBConnectionOptionsParser.KeyThreads] = threads.ToString();
        return this;
    }

    /// <summary>
    /// Sets the max memory in megabytes.
    /// </summary>
    public DuckDBConnectionBuilder WithMaxMemory(long megabytes) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(megabytes);
        _options = _options with { MaxMemoryMB = megabytes };
        _configuration[DuckDBConnectionOptionsParser.KeyMaxMemory] = DuckDBConnectionOptionsParser.FormatMaxMemory(megabytes);
        return this;
    }

    /// <summary>
    /// Sets the max memory using a size string (e.g., "8GB", "512MB", "1TB").
    /// </summary>
    public DuckDBConnectionBuilder WithMaxMemory(string size) {
        ArgumentException.ThrowIfNullOrWhiteSpace(size);
        var megabytes = DuckDBConnectionOptionsParser.ParseMaxMemory(size)
            ?? throw new ArgumentException($"Invalid memory size format: '{size}'. Expected format: number followed by unit (B, KB, MB, GB, TB).", nameof(size));
        _options = _options with { MaxMemoryMB = megabytes };
        _configuration[DuckDBConnectionOptionsParser.KeyMaxMemory] = size;
        return this;
    }

    /// <summary>
    /// Configures logging.
    /// </summary>
    /// <param name="enabled">Whether logging is enabled.</param>
    /// <param name="level">The logging level.</param>
    public DuckDBConnectionBuilder WithLogging(bool enabled, LogLevel level = LogLevel.Warning) {
        _options = _options with { Logging = new DuckDBLoggingOptions { Enabled = enabled, Level = level } };
        _configuration[DuckDBConnectionOptionsParser.KeyEnableLogging] = enabled ? "true" : "false";

        if (enabled) {
            var levelValue = level switch {
                LogLevel.Critical    => "CRITICAL",
                LogLevel.Error       => "ERROR",
                LogLevel.Warning     => "WARN",
                LogLevel.Information => "INFO",
                LogLevel.Debug       => "DEBUG",
                LogLevel.Trace       => "TRACE",
                _                    => throw new ArgumentOutOfRangeException(nameof(level))
            };
            _configuration[DuckDBConnectionOptionsParser.KeyLoggingLevel] = levelValue;
        }

        return this;
    }

    /// <summary>
    /// Sets a configuration option.
    /// Use this for any DuckDB configuration option not exposed as a typed method.
    /// </summary>
    public DuckDBConnectionBuilder WithConfiguration(string key, string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _configuration[DuckDBConnectionOptionsParser.NormalizeKey(key)] = value;
        return this;
    }

    /// <summary>
    /// Removes a configuration option.
    /// </summary>
    public DuckDBConnectionBuilder WithoutConfiguration(string key) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _configuration.Remove(DuckDBConnectionOptionsParser.NormalizeKey(key));
        return this;
    }

    /// <summary>
    /// Clears all configuration options (keeps data source).
    /// </summary>
    public DuckDBConnectionBuilder ClearConfiguration() {
        _configuration.Clear();
        return this;
    }

    /// <summary>
    /// Builds the connection options.
    /// </summary>
    /// <param name="strict">If true, validates configuration keys against DuckDB's native configuration.</param>
    /// <exception cref="InvalidDuckDbConnectionStringException">Thrown when strict is true and configuration contains unknown keys.</exception>
    public DuckDBConnectionOptions Build(bool strict = false) {
        if (strict)
            ValidateKeys(_configuration.Keys);

        return _options with {
            Configuration = new Dictionary<string, string>(_configuration, StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Implicitly converts to <see cref="DuckDBConnectionOptions"/>.
    /// Uses non-strict mode.
    /// </summary>
    public static implicit operator DuckDBConnectionOptions(DuckDBConnectionBuilder builder) => builder.Build();

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
}
