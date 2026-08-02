namespace Kurrent.Projectors.Transactional;

/// <summary>
/// Hands out the client a projection writes through — a connection, or whatever else a backend
/// writes with.
/// </summary>
/// <remarks>
/// An interface rather than a delegate because it has room to grow. A pooled provider will eventually
/// need a way to hand a client back; a <c>Func</c> could never gain one without breaking every caller.
/// Users who only need a lambda go through <c>ProjectorOptions.ResolveClient(...)</c>, which wraps one
/// in a provider.
/// </remarks>
[PublicAPI]
public interface IProjectionClientProvider<TClient> {
    /// <summary>Returns a client. Called once per projection run, by its transaction.</summary>
    ValueTask<TClient> GetClient(CancellationToken cancellationToken);
}

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

