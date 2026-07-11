using Kurrent.Surge.Mcp;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;
using Polly;

// DI extensions conventionally live in the Microsoft.Extensions.DependencyInjection namespace
// so they surface on IServiceCollection without an extra using.
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class DuckDBMcpTaskStoreServiceCollectionExtensions {
    /// <summary>
    /// Registers a DuckDB-backed <see cref="IMcpTaskStore"/> (ModelContextProtocol.Core 2.0) as a
    /// singleton. Singleton lifetime is required so tasks created on one request remain visible to
    /// <c>tasks/get</c> polls on later requests.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">DuckDB connection string. Use a file source for durability.</param>
    /// <param name="options">Optional store options (poll interval, default TTL).</param>
    /// <param name="pipelineBuilder">
    /// Optional resilience pipeline builder; the store appends its write-conflict retry to it.
    /// </param>
    public static IServiceCollection AddDuckDBMcpTaskStore(
        this IServiceCollection services,
        string connectionString,
        DuckDBTaskStoreOptions? options = null,
        ResiliencePipelineBuilder? pipelineBuilder = null
    ) {
        services.TryAddSingleton<IMcpTaskStore>(sp => new DuckDBMcpTaskStore(
            connectionString,
            options,
            sp.GetService<TimeProvider>(),
            pipelineBuilder));

        return services;
    }
}
