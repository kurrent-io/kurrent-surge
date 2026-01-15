namespace Kurrent.DuckDB;

/// <summary>
/// Builds <see cref="DuckLakeOptions"/> instances using a fluent API.
/// </summary>
public sealed class DuckLakeOptionsBuilder {
    DuckLakeOptions _options = new();
    readonly Dictionary<string, DuckLakeScopedSettings> _schemaSettings = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, DuckLakeScopedSettings> _tableSettings  = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sets the metadata catalog path (DuckDB file by default).
    /// </summary>
    public DuckLakeOptionsBuilder WithCatalog(string path) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _options = _options with { MetadataPath = path, CatalogType = DuckLakeCatalogType.DuckDB };
        return this;
    }

    /// <summary>
    /// Sets a SQLite catalog for metadata storage.
    /// </summary>
    public DuckLakeOptionsBuilder WithSQLiteCatalog(string path) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _options = _options with { MetadataPath = path, CatalogType = DuckLakeCatalogType.SQLite };
        return this;
    }

    /// <summary>
    /// Sets a PostgreSQL catalog for metadata storage.
    /// </summary>
    public DuckLakeOptionsBuilder WithPostgresCatalog(string connectionString) {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = _options with { MetadataPath = connectionString, CatalogType = DuckLakeCatalogType.Postgres };
        return this;
    }

    /// <summary>
    /// Sets a MySQL catalog for metadata storage.
    /// </summary>
    public DuckLakeOptionsBuilder WithMySqlCatalog(string connectionString) {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = _options with { MetadataPath = connectionString, CatalogType = DuckLakeCatalogType.MySQL };
        return this;
    }

    /// <summary>
    /// Configures metadata options.
    /// </summary>
    public DuckLakeOptionsBuilder ConfigureMetadata(Action<DuckLakeMetadataOptionsBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new DuckLakeMetadataOptionsBuilder(_options.Metadata);
        configure(builder);
        _options = _options with { Metadata = builder.Build() };
        return this;
    }

    /// <summary>
    /// Configures data storage options.
    /// </summary>
    public DuckLakeOptionsBuilder ConfigureData(Action<DuckLakeDataOptionsBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new DuckLakeDataOptionsBuilder(_options.Data);
        configure(builder);
        _options = _options with { Data = builder.Build() };
        return this;
    }

    /// <summary>
    /// Sets the data path for Parquet files.
    /// </summary>
    public DuckLakeOptionsBuilder WithDataPath(string path) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _options = _options with { Data = _options.Data with { Path = path } };
        return this;
    }

    /// <summary>
    /// Sets whether to create the DuckLake if it does not exist.
    /// </summary>
    public DuckLakeOptionsBuilder CreateIfNotExists(bool enabled = true) {
        _options = _options with { CreateIfNotExists = enabled };
        return this;
    }

    /// <summary>
    /// Sets whether to auto-migrate schema if required.
    /// </summary>
    public DuckLakeOptionsBuilder MigrateIfRequired(bool enabled = true) {
        _options = _options with { MigrateIfRequired = enabled };
        return this;
    }

    /// <summary>
    /// Sets the connection as read-only.
    /// </summary>
    public DuckLakeOptionsBuilder AsReadOnly() {
        _options = _options with { ReadOnly = true };
        return this;
    }

    /// <summary>
    /// Configures retry options for transaction conflicts.
    /// </summary>
    public DuckLakeOptionsBuilder ConfigureRetry(Action<DuckLakeRetryOptionsBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new DuckLakeRetryOptionsBuilder(_options.Retry);
        configure(builder);
        _options = _options with { Retry = builder.Build() };
        return this;
    }

    /// <summary>
    /// Configures global settings that apply to all tables.
    /// </summary>
    public DuckLakeOptionsBuilder ConfigureGlobal(Action<DuckLakeScopedSettingsBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new DuckLakeScopedSettingsBuilder(_options.GlobalSettings);
        configure(builder);
        _options = _options with { GlobalSettings = builder.Build() };
        return this;
    }

    /// <summary>
    /// Configures settings for a specific schema. Overrides global settings.
    /// Multiple calls for the same schema will build on existing settings.
    /// </summary>
    public DuckLakeOptionsBuilder ConfigureSchema(string schema, Action<DuckLakeScopedSettingsBuilder> configure) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentNullException.ThrowIfNull(configure);

        var existing = _schemaSettings.GetValueOrDefault(schema, DuckLakeScopedSettings.Default);
        var builder  = new DuckLakeScopedSettingsBuilder(existing);
        configure(builder);
        _schemaSettings[schema] = builder.Build();
        return this;
    }

    /// <summary>
    /// Configures settings for a specific table. Overrides schema and global settings.
    /// Multiple calls for the same table will build on existing settings.
    /// </summary>
    /// <param name="schema">The schema name.</param>
    /// <param name="table">The table name.</param>
    /// <param name="configure">Action to configure table settings.</param>
    public DuckLakeOptionsBuilder ConfigureTable(string schema, string table, Action<DuckLakeScopedSettingsBuilder> configure) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentNullException.ThrowIfNull(configure);

        var key      = $"{schema}.{table}";
        var existing = _tableSettings.GetValueOrDefault(key, DuckLakeScopedSettings.Default);
        var builder  = new DuckLakeScopedSettingsBuilder(existing);
        configure(builder);
        _tableSettings[key] = builder.Build();
        return this;
    }

    /// <summary>
    /// Configures settings for a specific table in the "main" schema.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="configure">Action to configure table settings.</param>
    public DuckLakeOptionsBuilder ConfigureTable(string table, Action<DuckLakeScopedSettingsBuilder> configure) =>
        ConfigureTable("main", table, configure);

    /// <summary>
    /// Builds the DuckLake options.
    /// </summary>
    public DuckLakeOptions Build() =>
        _options with {
            SchemaSettings = _schemaSettings.Count > 0
                ? new Dictionary<string, DuckLakeScopedSettings>(_schemaSettings, StringComparer.OrdinalIgnoreCase)
                : _options.SchemaSettings,
            TableSettings = _tableSettings.Count > 0
                ? new Dictionary<string, DuckLakeScopedSettings>(_tableSettings, StringComparer.OrdinalIgnoreCase)
                : _options.TableSettings
        };
}

/// <summary>
/// Builds <see cref="DuckLakeMetadataOptions"/>.
/// </summary>
public sealed class DuckLakeMetadataOptionsBuilder {
    DuckLakeMetadataOptions _options;
    readonly Dictionary<string, string> _parameters = new(StringComparer.OrdinalIgnoreCase);

    internal DuckLakeMetadataOptionsBuilder(DuckLakeMetadataOptions options) {
        _options = options;
        foreach (var (key, value) in options.Parameters)
            _parameters[key] = value;
    }

    /// <summary>
    /// Sets the attached catalog database name.
    /// </summary>
    public DuckLakeMetadataOptionsBuilder WithCatalog(string catalog) {
        _options = _options with { Catalog = catalog };
        return this;
    }

    /// <summary>
    /// Sets the schema for DuckLake tables.
    /// </summary>
    public DuckLakeMetadataOptionsBuilder WithSchema(string schema) {
        _options = _options with { Schema = schema };
        return this;
    }

    /// <summary>
    /// Adds a parameter for the catalog server connection.
    /// </summary>
    public DuckLakeMetadataOptionsBuilder WithParameter(string key, string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _parameters[key] = value;
        return this;
    }

    internal DuckLakeMetadataOptions Build() =>
        _options with {
            Parameters = _parameters.Count > 0
                ? new Dictionary<string, string>(_parameters, StringComparer.OrdinalIgnoreCase)
                : _options.Parameters
        };
}

/// <summary>
/// Builds <see cref="DuckLakeDataOptions"/>.
/// </summary>
public sealed class DuckLakeDataOptionsBuilder {
    DuckLakeDataOptions _options;

    internal DuckLakeDataOptionsBuilder(DuckLakeDataOptions options) => _options = options;

    /// <summary>
    /// Sets the data path for Parquet files.
    /// </summary>
    public DuckLakeDataOptionsBuilder WithPath(string path) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _options = _options with { Path = path };
        return this;
    }

    /// <summary>
    /// Sets whether to override the stored data path.
    /// </summary>
    public DuckLakeDataOptionsBuilder OverridePath(bool enabled = true) {
        _options = _options with { OverridePath = enabled };
        return this;
    }

    /// <summary>
    /// Enables encryption for data files.
    /// </summary>
    public DuckLakeDataOptionsBuilder WithEncryption(bool enabled = true) {
        _options = _options with { Encrypted = enabled };
        return this;
    }

    /// <summary>
    /// Sets the maximum rows to inline during insert operations.
    /// </summary>
    public DuckLakeDataOptionsBuilder WithInliningRowLimit(int limit) {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        _options = _options with { InliningRowLimit = limit };
        return this;
    }

    internal DuckLakeDataOptions Build() => _options;
}

/// <summary>
/// Builds <see cref="DuckLakeScopedSettings"/> for a specific scope (global, schema, or table).
/// </summary>
public sealed class DuckLakeScopedSettingsBuilder {
    DuckLakeScopedSettings _settings;
    readonly Dictionary<string, string> _customSettings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new builder with default settings.
    /// </summary>
    public DuckLakeScopedSettingsBuilder() : this(DuckLakeScopedSettings.Default) { }

    internal DuckLakeScopedSettingsBuilder(DuckLakeScopedSettings settings) {
        _settings = settings;
        foreach (var (key, value) in settings.Settings)
            _customSettings[key] = value;
    }

    /// <summary>
    /// Configures Parquet file options.
    /// </summary>
    public DuckLakeScopedSettingsBuilder ConfigureParquet(Action<DuckLakeParquetOptionsBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new DuckLakeParquetOptionsBuilder(_settings.Parquet);
        configure(builder);
        _settings = _settings with { Parquet = builder.Build() };
        return this;
    }

    /// <summary>
    /// Configures file output options.
    /// </summary>
    public DuckLakeScopedSettingsBuilder ConfigureFile(Action<DuckLakeFileOptionsBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new DuckLakeFileOptionsBuilder(_settings.File);
        configure(builder);
        _settings = _settings with { File = builder.Build() };
        return this;
    }

    /// <summary>
    /// Sets whether to require explicit commit messages.
    /// </summary>
    public DuckLakeScopedSettingsBuilder RequireCommitMessage(bool enabled = true) {
        _settings = _settings with { RequireCommitMessage = enabled };
        return this;
    }

    /// <summary>
    /// Sets the minimum deletion fraction (0-1) that triggers file rewrite.
    /// </summary>
    public DuckLakeScopedSettingsBuilder WithRewriteDeleteThreshold(double threshold) {
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(threshold, 1);
        _settings = _settings with { RewriteDeleteThreshold = threshold };
        return this;
    }

    /// <summary>
    /// Sets the data path for Parquet files in this scope.
    /// </summary>
    public DuckLakeScopedSettingsBuilder WithDataPath(string path) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _settings = _settings with { DataPath = path };
        return this;
    }

    /// <summary>
    /// Sets the maximum rows to inline during insert operations.
    /// </summary>
    public DuckLakeScopedSettingsBuilder WithDataInliningRowLimit(int limit) {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        _settings = _settings with { DataInliningRowLimit = limit };
        return this;
    }

    /// <summary>
    /// Sets the age threshold for file removal during vacuum.
    /// </summary>
    /// <param name="interval">Interval string, e.g., "7 days", "1 hour", "30 minutes".</param>
    public DuckLakeScopedSettingsBuilder WithDeleteOlderThan(string interval) {
        ArgumentException.ThrowIfNullOrWhiteSpace(interval);
        _settings = _settings with { DeleteOlderThan = interval };
        return this;
    }

    /// <summary>
    /// Sets the age threshold for file removal during vacuum.
    /// </summary>
    public DuckLakeScopedSettingsBuilder WithDeleteOlderThan(TimeSpan interval) {
        _settings = _settings with { DeleteOlderThan = FormatInterval(interval) };
        return this;
    }

    /// <summary>
    /// Sets the age threshold for snapshot expiration.
    /// </summary>
    /// <param name="interval">Interval string, e.g., "7 days", "1 hour", "30 minutes".</param>
    public DuckLakeScopedSettingsBuilder WithExpireOlderThan(string interval) {
        ArgumentException.ThrowIfNullOrWhiteSpace(interval);
        _settings = _settings with { ExpireOlderThan = interval };
        return this;
    }

    /// <summary>
    /// Sets the age threshold for snapshot expiration.
    /// </summary>
    public DuckLakeScopedSettingsBuilder WithExpireOlderThan(TimeSpan interval) {
        _settings = _settings with { ExpireOlderThan = FormatInterval(interval) };
        return this;
    }

    /// <summary>
    /// Sets the pre-defined schema for compaction operations.
    /// </summary>
    public DuckLakeScopedSettingsBuilder WithCompactionSchema(string schema) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        _settings = _settings with { CompactionSchema = schema };
        return this;
    }

    /// <summary>
    /// Sets the pre-defined table for compaction operations.
    /// </summary>
    public DuckLakeScopedSettingsBuilder WithCompactionTable(string table) {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        _settings = _settings with { CompactionTable = table };
        return this;
    }

    /// <summary>
    /// Sets both schema and table for compaction operations.
    /// </summary>
    public DuckLakeScopedSettingsBuilder WithCompactionTarget(string schema, string table) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        _settings = _settings with { CompactionSchema = schema, CompactionTable = table };
        return this;
    }

    /// <summary>
    /// Sets a custom setting not exposed as a typed property.
    /// </summary>
    public DuckLakeScopedSettingsBuilder WithSetting(string key, string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _customSettings[key] = value;
        return this;
    }

    internal DuckLakeScopedSettings Build() =>
        _settings with {
            Settings = _customSettings.Count > 0
                ? new Dictionary<string, string>(_customSettings, StringComparer.OrdinalIgnoreCase)
                : _settings.Settings
        };

    static string FormatInterval(TimeSpan interval) {
        if (interval.TotalDays >= 1 && Math.Abs(interval.TotalDays - Math.Floor(interval.TotalDays)) < 0.001)
            return $"{(int)interval.TotalDays} days";
        if (interval.TotalHours >= 1 && Math.Abs(interval.TotalHours - Math.Floor(interval.TotalHours)) < 0.001)
            return $"{(int)interval.TotalHours} hours";
        if (interval.TotalMinutes >= 1 && Math.Abs(interval.TotalMinutes - Math.Floor(interval.TotalMinutes)) < 0.001)
            return $"{(int)interval.TotalMinutes} minutes";
        return $"{(int)interval.TotalSeconds} seconds";
    }
}

/// <summary>
/// Builds <see cref="DuckLakeParquetOptions"/>.
/// </summary>
public sealed class DuckLakeParquetOptionsBuilder {
    DuckLakeParquetOptions _options;

    internal DuckLakeParquetOptionsBuilder(DuckLakeParquetOptions options) => _options = options;

    /// <summary>
    /// Sets the compression algorithm.
    /// </summary>
    public DuckLakeParquetOptionsBuilder WithCompression(ParquetCompression compression) {
        _options = _options with { Compression = compression };
        return this;
    }

    /// <summary>
    /// Sets the compression level.
    /// </summary>
    public DuckLakeParquetOptionsBuilder WithCompressionLevel(int level) {
        _options = _options with { CompressionLevel = level };
        return this;
    }

    /// <summary>
    /// Sets the Parquet format version.
    /// </summary>
    public DuckLakeParquetOptionsBuilder WithVersion(int version) {
        _options = _options with { Version = version };
        return this;
    }

    /// <summary>
    /// Sets the number of rows per row group.
    /// </summary>
    public DuckLakeParquetOptionsBuilder WithRowGroupSize(int rows) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        _options = _options with { RowGroupSize = rows };
        return this;
    }

    /// <summary>
    /// Sets the byte-based row group size limit.
    /// </summary>
    public DuckLakeParquetOptionsBuilder WithRowGroupSizeBytes(long bytes) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        _options = _options with { RowGroupSizeBytes = bytes };
        return this;
    }

    internal DuckLakeParquetOptions Build() => _options;
}

/// <summary>
/// Builds <see cref="DuckLakeFileOptions"/>.
/// </summary>
public sealed class DuckLakeFileOptionsBuilder {
    DuckLakeFileOptions _options;

    internal DuckLakeFileOptionsBuilder(DuckLakeFileOptions options) => _options = options;

    /// <summary>
    /// Sets the target size for data files (e.g., "512MB", "1GB").
    /// </summary>
    public DuckLakeFileOptionsBuilder WithTargetSize(string size) {
        ArgumentException.ThrowIfNullOrWhiteSpace(size);
        _options = _options with { TargetSize = size };
        return this;
    }

    /// <summary>
    /// Sets the target size for data files in megabytes.
    /// </summary>
    public DuckLakeFileOptionsBuilder WithTargetSize(long megabytes) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(megabytes);
        _options = _options with { TargetSize = DuckDBConnectionOptionsParser.FormatMaxMemory(megabytes) };
        return this;
    }

    /// <summary>
    /// Sets whether to use Hive-style partitioning folder structure.
    /// </summary>
    public DuckLakeFileOptionsBuilder WithHivePattern(bool enabled = true) {
        _options = _options with { HivePattern = enabled };
        return this;
    }

    /// <summary>
    /// Enables encryption for data files.
    /// </summary>
    public DuckLakeFileOptionsBuilder WithEncryption(bool enabled = true) {
        _options = _options with { Encrypted = enabled };
        return this;
    }

    /// <summary>
    /// Sets whether to create separate output files per thread.
    /// </summary>
    public DuckLakeFileOptionsBuilder WithPerThreadOutput(bool enabled = true) {
        _options = _options with { PerThreadOutput = enabled };
        return this;
    }

    internal DuckLakeFileOptions Build() => _options;
}

/// <summary>
/// Builds <see cref="DuckLakeRetryOptions"/>.
/// </summary>
public sealed class DuckLakeRetryOptionsBuilder {
    DuckLakeRetryOptions _options;

    internal DuckLakeRetryOptionsBuilder(DuckLakeRetryOptions options) => _options = options;

    /// <summary>
    /// Sets the maximum number of retry attempts.
    /// </summary>
    public DuckLakeRetryOptionsBuilder WithMaxRetryCount(int count) {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _options = _options with { MaxRetryCount = count };
        return this;
    }

    /// <summary>
    /// Sets the initial wait time in milliseconds between retries.
    /// </summary>
    public DuckLakeRetryOptionsBuilder WithRetryWaitMs(int milliseconds) {
        ArgumentOutOfRangeException.ThrowIfNegative(milliseconds);
        _options = _options with { RetryWaitMs = milliseconds };
        return this;
    }

    /// <summary>
    /// Sets the backoff multiplier for exponential retry delays.
    /// </summary>
    public DuckLakeRetryOptionsBuilder WithRetryBackoff(double multiplier) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(multiplier);
        _options = _options with { RetryBackoff = multiplier };
        return this;
    }

    internal DuckLakeRetryOptions Build() => _options;
}
