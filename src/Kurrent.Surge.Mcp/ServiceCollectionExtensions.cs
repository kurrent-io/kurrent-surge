using Kurrent.Surge.Mcp;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// DI extensions conventionally live in the Microsoft.Extensions.DependencyInjection namespace
// so they surface on IServiceCollection without an extra using.
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class DuckDBMcpTaskStoreServiceCollectionExtensions {
    /// <summary>
    /// Registers a DuckDB-backed <see cref="IMcpTaskStore"/> (ModelContextProtocol.Core 2.0) as a
    /// singleton. Singleton lifetime is required by the contract: <c>tasks/get</c> polls on later
    /// requests must observe tasks created on earlier ones, so all server instances must share it.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">DuckDB connection string. Use a file source for durability.</param>
    /// <param name="options">Optional store options (TTL, limits, cleanup cadence).</param>
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
