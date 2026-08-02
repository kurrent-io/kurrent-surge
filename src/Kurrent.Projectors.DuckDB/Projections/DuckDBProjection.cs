using DuckDB.NET.Data;
using Kurrent.Quack;

namespace Kurrent.Projectors.DuckDB;

/// <summary>
/// Closed <see cref="Projection{TClient}"/> for DuckDB, so callers never type
/// <see cref="DuckDBAdvancedConnection"/> as a type argument.
/// </summary>
[PublicAPI]
public abstract class DuckDBProjection : Projection<DuckDBAdvancedConnection>;
