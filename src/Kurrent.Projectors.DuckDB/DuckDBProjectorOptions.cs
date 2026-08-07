using Kurrent.Quack;

namespace Kurrent.Projectors.DuckDB;

[PublicAPI]
public record DuckDBProjectorOptions : ProjectorOptions<DuckDBAdvancedConnection>;
