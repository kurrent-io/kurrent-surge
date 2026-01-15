namespace Kurrent.DuckDB;

/// <summary>
/// Specifies the catalog database type for DuckLake metadata storage.
/// </summary>
public enum DuckLakeCatalogType {
    /// <summary>
    /// DuckDB file for metadata storage (default).
    /// </summary>
    DuckDB,

    /// <summary>
    /// SQLite file for metadata storage.
    /// </summary>
    SQLite,

    /// <summary>
    /// PostgreSQL database for metadata storage.
    /// </summary>
    Postgres,

    /// <summary>
    /// MySQL database for metadata storage.
    /// </summary>
    MySQL
}

/// <summary>
/// Specifies the Parquet compression algorithm.
/// </summary>
public enum ParquetCompression {
    /// <summary>
    /// No compression.
    /// </summary>
    Uncompressed,

    /// <summary>
    /// Snappy compression (default, fast).
    /// </summary>
    Snappy,

    /// <summary>
    /// Gzip compression.
    /// </summary>
    Gzip,

    /// <summary>
    /// Zstandard compression (good balance of speed and ratio).
    /// </summary>
    Zstd,

    /// <summary>
    /// Brotli compression.
    /// </summary>
    Brotli,

    /// <summary>
    /// LZ4 compression.
    /// </summary>
    Lz4,

    /// <summary>
    /// LZ4 raw compression.
    /// </summary>
    Lz4Raw
}
