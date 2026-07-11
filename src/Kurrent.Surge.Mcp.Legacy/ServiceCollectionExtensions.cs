using Kurrent.Surge.Mcp;
using Kurrent.Surge.Mcp.Legacy;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

// DI extensions conventionally live in the Microsoft.Extensions.DependencyInjection namespace.
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class DuckDBMcpTaskStoreServiceCollectionExtensions {
    /// <summary>
    /// Registers a DuckDB-backed <see cref="IMcpTaskStore"/> (ModelContextProtocol.Core 1.4.1) as a
    /// singleton. Singleton lifetime is required so tasks created on one request remain visible to
    /// <c>tasks/get</c> polls and listings on later requests.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">DuckDB connection string. Use a file source for durability.</param>
    /// <param name="options">Optional store options (TTL, limits, page size, cleanup cadence).</param>
    public static IServiceCollection AddDuckDBMcpTaskStore(
        this IServiceCollection services,
        DuckDBConnectionString connectionString,
        DuckDBTaskStoreOptions? options = null
    ) {
        services.TryAddSingleton<IMcpTaskStore>(sp => new DuckDBMcpTaskStore(
            connectionString,
            options,
            sp.GetService<TimeProvider>(),
            sp.GetService<ILogger<DuckDBTaskStore>>()));

        return services;
    }
}
