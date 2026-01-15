namespace Kurrent.DuckDB;

/// <summary>
/// Specifies the type of DuckDB data source.
/// </summary>
public enum DataSourceType {
    /// <summary>
    /// In-memory database. No data is persisted to disk.
    /// </summary>
    InMemory,

    /// <summary>
    /// File-based persistent database.
    /// </summary>
    File,

    /// <summary>
    /// MotherDuck cloud database.
    /// </summary>
    MotherDuck,

    /// <summary>
    /// DuckLake data lakehouse format.
    /// </summary>
    DuckLake
}
