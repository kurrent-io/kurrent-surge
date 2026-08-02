using Kurrent.Quack;
using Kurrent.Projectors.Transactional;

namespace Kurrent.Projectors.DuckDB;

/// <summary>
/// Closed <see cref="UpsertProjection{TClient,TDocument}"/> for DuckDB.
/// </summary>
/// <remarks>
/// Fully qualified for the same reason as <see cref="DuckDBProjection"/>'s base.
/// </remarks>
[PublicAPI]
public abstract class DuckDBUpsertProjection<TDocument> : UpsertProjection<DuckDBAdvancedConnection, TDocument> where TDocument : class;