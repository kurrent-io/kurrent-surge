using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Client;

/// <summary>
/// Scoped registration surface for Kurrent components. Packages extend this
/// (client, processors) instead of <see cref="IServiceCollection"/>, so all
/// Kurrent registrations hang off a single <c>AddKurrent</c> entry point.
/// </summary>
[PublicAPI]
public interface IKurrentBuilder {
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
