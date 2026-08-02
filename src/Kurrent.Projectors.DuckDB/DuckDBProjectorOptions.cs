using Kurrent.Quack;

namespace Kurrent.Projectors.DuckDB;

/// <summary>
/// Options for a DuckDB-backed projection. Everything a caller can configure is declared on
/// <see cref="ProjectorOptions{TClient}"/> or here — there is no processor surface to hide.
/// </summary>
[PublicAPI]
public record DuckDBProjectorOptions : ProjectorOptions<DuckDBAdvancedConnection>;
