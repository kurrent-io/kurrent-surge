using DuckDB.NET.Data;

namespace Kurrent.DuckDB;

public static partial class DuckDBConnectionExtensions {
    extension(DuckDBConnection) {
        /// <summary>
        /// Creates a builder for a MotherDuck cloud database.
        /// </summary>
        /// <param name="database">The MotherDuck database name.</param>
        /// <param name="token">Optional authentication token.</param>
        public static DuckDBConnectionBuilder ForMotherDuck(string database, string? token = null) {
            ArgumentException.ThrowIfNullOrWhiteSpace(database);
            return new DuckDBConnectionBuilder().ConfigureMotherDuck(database, token);
        }
    }
}
