using DuckDB.NET.Data;

namespace Kurrent.DuckDB;

public static partial class DuckDBConnectionExtensions {
    extension(DuckDBConnection) {
        /// <summary>
        /// Creates a builder for a DuckLake data lakehouse with default settings.
        /// </summary>
        public static DuckDBConnectionBuilder ForDuckLake() =>
            new DuckDBConnectionBuilder().ConfigureDuckLake(new DuckLakeOptions());

        /// <summary>
        /// Creates a builder for a DuckLake data lakehouse with the specified metadata path.
        /// </summary>
        /// <param name="metadataPath">Path to the DuckLake metadata catalog.</param>
        public static DuckDBConnectionBuilder ForDuckLake(string metadataPath) {
            ArgumentException.ThrowIfNullOrWhiteSpace(metadataPath);
            return new DuckDBConnectionBuilder().ConfigureDuckLake(new DuckLakeOptions { MetadataPath = metadataPath });
        }

        /// <summary>
        /// Creates a builder for a DuckLake data lakehouse with custom configuration.
        /// </summary>
        /// <param name="configure">Action to configure DuckLake options.</param>
        public static DuckDBConnectionBuilder ForDuckLake(Action<DuckLakeOptionsBuilder> configure) {
            ArgumentNullException.ThrowIfNull(configure);

            var optionsBuilder = new DuckLakeOptionsBuilder();
            configure(optionsBuilder);
            var options = optionsBuilder.Build();

            return new DuckDBConnectionBuilder().ConfigureDuckLake(options);
        }
    }


}
