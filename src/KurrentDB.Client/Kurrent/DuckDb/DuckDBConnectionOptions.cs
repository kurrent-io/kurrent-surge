namespace Kurrent.DuckDB;

/// <summary>
/// Represents DuckDB connection configuration options.
/// Use <see cref="DuckDBConnection"/> factory methods to create instances.
/// </summary>
public readonly record struct DuckDBConnectionOptions {
    /// <summary>
    /// Gets an in-memory connection options instance.
    /// </summary>
    public static readonly DuckDBConnectionOptions Default = new();

    /// <summary>
    /// Creates a new in-memory DuckDB connection options instance.
    /// </summary>
    public DuckDBConnectionOptions() { }

    /// <summary>
    /// Gets the data source type.
    /// </summary>
    public DataSourceType Type { get; init; } = DataSourceType.InMemory;

    /// <summary>
    /// Gets the data source path or identifier.
    /// </summary>
    public string DataSource { get; init; } = DuckDBConnectionOptionsParser.InMemoryDataSource;

    /// <summary>
    /// Gets whether the connection is read-only.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Gets the number of threads. Zero means use DuckDB default.
    /// </summary>
    public int Threads { get; init; }

    /// <summary>
    /// Gets the max memory in megabytes. Zero means use DuckDB default.
    /// </summary>
    public long MaxMemoryMB { get; init; }

    /// <summary>
    /// Gets the logging configuration.
    /// </summary>
    public DuckDBLoggingOptions Logging { get; init; } = DuckDBLoggingOptions.Default;

    /// <summary>
    /// Gets MotherDuck-specific options.
    /// </summary>
    public MotherDuckOptions MotherDuck { get; init; } = MotherDuckOptions.Default;

    /// <summary>
    /// Gets DuckLake-specific options.
    /// </summary>
    public DuckLakeOptions DuckLake { get; init; } = DuckLakeOptions.Default;

    /// <summary>
    /// Gets all configuration options as key-value pairs.
    /// </summary>
    public IReadOnlyDictionary<string, string> Configuration { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets whether this is an in-memory database connection.
    /// </summary>
    public bool IsInMemory => Type == DataSourceType.InMemory;

    /// <summary>
    /// Gets whether this is a file-based database connection.
    /// </summary>
    public bool IsFile => Type == DataSourceType.File;

    /// <summary>
    /// Gets whether this is a MotherDuck cloud connection.
    /// </summary>
    public bool IsMotherDuck => Type == DataSourceType.MotherDuck;

    /// <summary>
    /// Gets whether this is a DuckLake connection.
    /// </summary>
    public bool IsDuckLake => Type == DataSourceType.DuckLake;

    /// <summary>
    /// Converts the options to an ADO.NET-compatible connection string.
    /// </summary>
    public string ToConnectionString() => DuckDBConnectionOptionsParser.ToConnectionString(this);

    /// <inheritdoc />
    public override string ToString() => ToConnectionString();

    /// <summary>
    /// Implicit conversion to connection string.
    /// </summary>
    public static implicit operator string(DuckDBConnectionOptions options) => options.ToConnectionString();

    /// <summary>
    /// Implicit conversion from builder.
    /// </summary>
    public static implicit operator DuckDBConnectionOptions(DuckDBConnectionBuilder builder) => builder.Build();
}
