namespace Kurrent.DuckDB;

/// <summary>
/// Configuration options for DuckLake data lakehouse connections.
/// </summary>
public sealed record DuckLakeOptions {
    /// <summary>
    /// Default DuckLake options.
    /// </summary>
    public static readonly DuckLakeOptions Default = new();

    /// <summary>
    /// Gets the path to the metadata catalog (file path or connection string).
    /// </summary>
    public string MetadataPath { get; init; } = "";

    /// <summary>
    /// Gets the catalog database type.
    /// </summary>
    public DuckLakeCatalogType CatalogType { get; init; } = DuckLakeCatalogType.DuckDB;

    /// <summary>
    /// Gets metadata-related options.
    /// </summary>
    public DuckLakeMetadataOptions Metadata { get; init; } = DuckLakeMetadataOptions.Default;

    /// <summary>
    /// Gets data storage options.
    /// </summary>
    public DuckLakeDataOptions Data { get; init; } = DuckLakeDataOptions.Default;

    /// <summary>
    /// Gets whether to create the DuckLake if it does not exist. Default: true.
    /// </summary>
    public bool CreateIfNotExists { get; init; } = true;

    /// <summary>
    /// Gets whether to auto-migrate schema if required. Default: true.
    /// </summary>
    public bool MigrateIfRequired { get; init; } = true;

    /// <summary>
    /// Gets whether the connection is read-only. Default: false.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// Gets transaction retry options.
    /// </summary>
    public DuckLakeRetryOptions Retry { get; init; } = DuckLakeRetryOptions.Default;

    /// <summary>
    /// Gets the global settings applied to all tables.
    /// </summary>
    public DuckLakeScopedSettings GlobalSettings { get; init; } = DuckLakeScopedSettings.Default;

    /// <summary>
    /// Gets schema-specific settings. Key is the schema name.
    /// </summary>
    public IReadOnlyDictionary<string, DuckLakeScopedSettings> SchemaSettings { get; init; } =
        new Dictionary<string, DuckLakeScopedSettings>();

    /// <summary>
    /// Gets table-specific settings. Key is the fully qualified table name (schema.table).
    /// </summary>
    public IReadOnlyDictionary<string, DuckLakeScopedSettings> TableSettings { get; init; } =
        new Dictionary<string, DuckLakeScopedSettings>();

    /// <summary>
    /// Converts all options to a dictionary of key-value settings.
    /// Includes metadata, data, behavior, retry, and maintenance options.
    /// </summary>
    public Dictionary<string, string> ToDictionary() {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Metadata options
        if (!string.IsNullOrEmpty(Metadata.Catalog))
            dict["metadata_catalog"] = Metadata.Catalog;
        if (Metadata.Schema != "main")
            dict["metadata_schema"] = Metadata.Schema;

        // Data options
        if (!string.IsNullOrEmpty(Data.Path))
            dict["data_path"] = Data.Path;
        if (!Data.OverridePath)
            dict["override_data_path"] = "false";
        if (Data.Encrypted)
            dict["encrypted"] = "true";
        if (Data.InliningRowLimit > 0)
            dict["data_inlining_row_limit"] = Data.InliningRowLimit.ToString();

        // Behavior options
        if (!CreateIfNotExists)
            dict["create_if_not_exists"] = "false";
        if (!MigrateIfRequired)
            dict["migrate_if_required"] = "false";
        if (ReadOnly)
            dict["read_only"] = "true";

        // Retry options
        if (Retry.MaxRetryCount != 10)
            dict["ducklake_max_retry_count"] = Retry.MaxRetryCount.ToString();
        if (Retry.RetryWaitMs != 100)
            dict["ducklake_retry_wait_ms"] = Retry.RetryWaitMs.ToString();
        if (Math.Abs(Retry.RetryBackoff - 1.5) > 0.001)
            dict["ducklake_retry_backoff"] = Retry.RetryBackoff.ToString("F2");

        // Global settings (includes parquet, file, maintenance options)
        foreach (var (key, value) in GlobalSettings.ToDictionary())
            dict[key] = value;

        return dict;
    }
}

/// <summary>
/// Metadata configuration options for DuckLake.
/// </summary>
public sealed record DuckLakeMetadataOptions {
    /// <summary>
    /// Default metadata options.
    /// </summary>
    public static readonly DuckLakeMetadataOptions Default = new();

    /// <summary>
    /// Gets the attached catalog database name. Default: auto-generated from DuckLake name.
    /// </summary>
    public string Catalog { get; init; } = "";

    /// <summary>
    /// Gets the schema for DuckLake tables. Default: "main".
    /// </summary>
    public string Schema { get; init; } = "main";

    /// <summary>
    /// Gets additional parameters for the catalog server connection.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Data storage options for DuckLake.
/// </summary>
public sealed record DuckLakeDataOptions {
    /// <summary>
    /// Default data options.
    /// </summary>
    public static readonly DuckLakeDataOptions Default = new();

    /// <summary>
    /// Gets the path where data files (Parquet) are stored.
    /// Can be local path, s3://, az://, gs://, or r2://.
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// Gets whether to override the stored data path for this connection. Default: true.
    /// </summary>
    public bool OverridePath { get; init; } = true;

    /// <summary>
    /// Gets whether data files should be encrypted. Default: false.
    /// </summary>
    public bool Encrypted { get; init; }

    /// <summary>
    /// Gets the maximum rows to inline during insert operations. Default: 0 (disabled).
    /// </summary>
    public int InliningRowLimit { get; init; }
}

/// <summary>
/// Scoped settings for DuckLake that can be applied at global, schema, or table level.
/// More specific scopes override less specific ones.
/// </summary>
public sealed record DuckLakeScopedSettings {
    /// <summary>
    /// Default scoped settings.
    /// </summary>
    public static readonly DuckLakeScopedSettings Default = new();

    /// <summary>
    /// Gets Parquet file configuration options.
    /// </summary>
    public DuckLakeParquetOptions Parquet { get; init; } = DuckLakeParquetOptions.Default;

    /// <summary>
    /// Gets file output configuration options.
    /// </summary>
    public DuckLakeFileOptions File { get; init; } = DuckLakeFileOptions.Default;

    /// <summary>
    /// Gets the path where data files (Parquet) are stored for this scope.
    /// Can be local path, s3://, az://, gs://, or r2://. Empty means inherit from parent scope.
    /// </summary>
    public string DataPath { get; init; } = "";

    /// <summary>
    /// Gets the maximum rows to inline during insert operations. Zero means inherit from parent scope.
    /// </summary>
    public int DataInliningRowLimit { get; init; }

    /// <summary>
    /// Gets whether to require explicit commit messages. Default: false.
    /// </summary>
    public bool RequireCommitMessage { get; init; }

    /// <summary>
    /// Gets the minimum deletion fraction (0-1) that triggers file rewrite. Default: 0.95.
    /// </summary>
    public double RewriteDeleteThreshold { get; init; } = 0.95;

    /// <summary>
    /// Gets the age threshold for file removal during vacuum operations.
    /// Empty means not set. Example values: "7 days", "1 hour".
    /// </summary>
    public string DeleteOlderThan { get; init; } = "";

    /// <summary>
    /// Gets the age threshold for snapshot expiration.
    /// Empty means not set. Example values: "7 days", "1 hour".
    /// </summary>
    public string ExpireOlderThan { get; init; } = "";

    /// <summary>
    /// Gets the pre-defined schema name for compaction operations. Empty means not set.
    /// </summary>
    public string CompactionSchema { get; init; } = "";

    /// <summary>
    /// Gets the pre-defined table name for compaction operations. Empty means not set.
    /// </summary>
    public string CompactionTable { get; init; } = "";

    /// <summary>
    /// Gets additional settings as key-value pairs for options not exposed as typed properties.
    /// </summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Converts the settings to a dictionary for DuckLake configuration.
    /// </summary>
    public Dictionary<string, string> ToDictionary() {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Data options
        if (!string.IsNullOrEmpty(DataPath))
            dict["data_path"] = DataPath;
        if (DataInliningRowLimit > 0)
            dict["data_inlining_row_limit"] = DataInliningRowLimit.ToString();

        // Parquet options
        if (Parquet.Compression != ParquetCompression.Snappy)
            dict["parquet_compression"] = Parquet.Compression.ToString().ToLowerInvariant();
        if (Parquet.CompressionLevel != 3)
            dict["parquet_compression_level"] = Parquet.CompressionLevel.ToString();
        if (Parquet.Version != 1)
            dict["parquet_version"] = Parquet.Version.ToString();
        if (Parquet.RowGroupSize != 122880)
            dict["parquet_row_group_size"] = Parquet.RowGroupSize.ToString();
        if (Parquet.RowGroupSizeBytes > 0)
            dict["parquet_row_group_size_bytes"] = Parquet.RowGroupSizeBytes.ToString();

        // File options
        if (File.TargetSize != "512MB")
            dict["target_file_size"] = File.TargetSize;
        if (!File.HivePattern)
            dict["hive_file_pattern"] = "false";
        if (File.Encrypted)
            dict["encrypted"] = "true";
        if (File.PerThreadOutput)
            dict["per_thread_output"] = "true";

        // Behavior options
        if (RequireCommitMessage)
            dict["require_commit_message"] = "true";
        if (Math.Abs(RewriteDeleteThreshold - 0.95) > 0.001)
            dict["rewrite_delete_threshold"] = RewriteDeleteThreshold.ToString("F2");

        // Maintenance options
        if (!string.IsNullOrEmpty(DeleteOlderThan))
            dict["delete_older_than"] = DeleteOlderThan;
        if (!string.IsNullOrEmpty(ExpireOlderThan))
            dict["expire_older_than"] = ExpireOlderThan;
        if (!string.IsNullOrEmpty(CompactionSchema))
            dict["compaction_schema"] = CompactionSchema;
        if (!string.IsNullOrEmpty(CompactionTable))
            dict["compaction_table"] = CompactionTable;

        // Custom settings
        foreach (var (key, value) in Settings)
            dict[key] = value;

        return dict;
    }
}

/// <summary>
/// Parquet file configuration options for DuckLake.
/// </summary>
public sealed record DuckLakeParquetOptions {
    /// <summary>
    /// Default Parquet options.
    /// </summary>
    public static readonly DuckLakeParquetOptions Default = new();

    /// <summary>
    /// Gets the compression algorithm. Default: Snappy.
    /// </summary>
    public ParquetCompression Compression { get; init; } = ParquetCompression.Snappy;

    /// <summary>
    /// Gets the compression level. Default: 3.
    /// </summary>
    public int CompressionLevel { get; init; } = 3;

    /// <summary>
    /// Gets the Parquet format version. Default: 1.
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// Gets the number of rows per row group. Default: 122880.
    /// </summary>
    public int RowGroupSize { get; init; } = 122880;

    /// <summary>
    /// Gets the byte-based row group size limit. Zero means not set.
    /// </summary>
    public long RowGroupSizeBytes { get; init; }
}

/// <summary>
/// File output configuration options for DuckLake.
/// </summary>
public sealed record DuckLakeFileOptions {
    /// <summary>
    /// Default file options.
    /// </summary>
    public static readonly DuckLakeFileOptions Default = new();

    /// <summary>
    /// Gets the target size for data files. Default: "512MB".
    /// </summary>
    public string TargetSize { get; init; } = "512MB";

    /// <summary>
    /// Gets whether to use Hive-style partitioning folder structure. Default: true.
    /// </summary>
    public bool HivePattern { get; init; } = true;

    /// <summary>
    /// Gets whether data files should be encrypted. Default: false.
    /// </summary>
    public bool Encrypted { get; init; }

    /// <summary>
    /// Gets whether to create separate output files per thread. Default: false.
    /// </summary>
    public bool PerThreadOutput { get; init; }
}

/// <summary>
/// Transaction retry configuration options for DuckLake.
/// These are applied via SET commands, not ATTACH parameters.
/// </summary>
public sealed record DuckLakeRetryOptions {
    /// <summary>
    /// Default retry options.
    /// </summary>
    public static readonly DuckLakeRetryOptions Default = new();

    /// <summary>
    /// Gets the maximum number of retry attempts for failed transactions. Default: 10.
    /// </summary>
    public int MaxRetryCount { get; init; } = 10;

    /// <summary>
    /// Gets the initial wait time in milliseconds between retries. Default: 100.
    /// </summary>
    public int RetryWaitMs { get; init; } = 100;

    /// <summary>
    /// Gets the backoff multiplier for exponential retry delays. Default: 1.5.
    /// </summary>
    public double RetryBackoff { get; init; } = 1.5;

    /// <summary>
    /// Converts to a dictionary of SET configuration options.
    /// Only includes non-default values.
    /// </summary>
    public Dictionary<string, string> ToDictionary() {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (MaxRetryCount != 10)
            dict["ducklake_max_retry_count"] = MaxRetryCount.ToString();
        if (RetryWaitMs != 100)
            dict["ducklake_retry_wait_ms"] = RetryWaitMs.ToString();
        if (Math.Abs(RetryBackoff - 1.5) > 0.001)
            dict["ducklake_retry_backoff"] = RetryBackoff.ToString("F2");

        return dict;
    }
}
