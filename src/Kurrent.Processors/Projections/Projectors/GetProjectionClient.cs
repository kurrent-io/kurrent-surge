namespace Kurrent.Projections;

/// <summary>
/// Resolves the client used by a projection. User-supplied via
/// <see cref="ProjectorOptions{TClient}.ResolveClient"/> (or an integration-specific equivalent).
/// <remarks>
/// Caching, pooling, and fresh-per-call semantics are the responsibility of the delegate
/// implementation. The framework calls this during <c>OnConnected</c> (for <c>Setup</c>) and
/// on every record dispatch that uses a with-client handler overload.
/// </remarks>
/// </summary>
public delegate ValueTask<TClient> GetProjectionClient<TClient>(CancellationToken ct);
