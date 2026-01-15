using DuckDB.NET.Data;

namespace Kurrent.DuckDB;

/// <summary>
/// Entry point for creating DuckDB connections.
/// Use the static factory methods to create connection builders.
/// </summary>
public static partial class DuckDBConnectionExtensions {
    extension(DuckDBConnection) {
        /// <summary>
        /// Gets an in-memory connection options instance.
        /// </summary>
        public static DuckDBConnectionOptions Default => DuckDBConnectionOptions.Default;

        /// <summary>
        /// Creates a builder for an in-memory database.
        /// </summary>
        /// <param name="shared">If true, creates a shared in-memory database accessible across connections.</param>
        public static DuckDBConnectionBuilder ForInMemory(bool shared = false) =>
            new DuckDBConnectionBuilder().ConfigureInMemory(shared);

        /// <summary>
        /// Creates a builder for a file-based database.
        /// </summary>
        /// <param name="path">Path to the database file. Must have extension: .ddb, .db, .quack, or .duckdb</param>
        /// <exception cref="ArgumentException">Thrown when the path has an invalid extension.</exception>
        public static DuckDBConnectionBuilder ForDatabase(string path) =>
            new DuckDBConnectionBuilder().ConfigureFile(path);

        /// <summary>
        /// Parses a connection string into connection options.
        /// </summary>
        /// <param name="connectionString">The connection string to parse.</param>
        /// <param name="strict">If true, validates configuration keys against DuckDB's native configuration.</param>
        /// <exception cref="InvalidDuckDbConnectionStringException">Thrown when the connection string is invalid.</exception>
        public static DuckDBConnectionOptions Parse(string connectionString, bool strict = false) =>
            DuckDBConnectionOptionsParser.Parse(connectionString, strict);
    }
}
