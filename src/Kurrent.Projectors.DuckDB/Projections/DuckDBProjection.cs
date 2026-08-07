using DuckDB.NET.Data;
using Kurrent.Quack;

namespace Kurrent.Projectors.DuckDB;

[PublicAPI]
public abstract class DuckDBProjection : Projection<DuckDBAdvancedConnection>;
