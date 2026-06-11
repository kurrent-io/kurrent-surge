namespace Kurrent.Projectors.DuckDB;

/// <summary>
/// Options for a DuckDB-backed projection. Inherits <see cref="ProjectorOptions{TClient}"/>
/// with <c>TClient = IDuckDBConnectionProvider</c>. Configure the projection by setting
/// <see cref="ProjectorOptions{TClient}.ResolveClient"/> to a delegate that returns your
/// <see cref="IDuckDBConnectionProvider"/>.
/// </summary>
[PublicAPI]
public record DuckDBProjectorOptions : ProjectorOptions<IDuckDBConnectionProvider>;
