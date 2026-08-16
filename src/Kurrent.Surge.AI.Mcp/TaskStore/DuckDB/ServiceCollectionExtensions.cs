using Kurrent.Surge.AI.Mcp.DuckDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Extensions.Tasks;
using Polly;

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
    /// <param name="options">
    /// Store options. When <see langword="null"/> they are resolved from a registered
    /// <see cref="DuckDBMcpTaskStoreOptions"/>, then bound from the
    /// <see cref="DuckDBMcpTaskStoreOptions.ConfigurationSection"/> section of a registered
    /// <see cref="IConfiguration"/>, then defaulted.
    /// </param>
    /// <param name="pipelineBuilder">
    /// Optional resilience pipeline; when <see langword="null"/> the store's default write-conflict retry is used.
    /// </param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddDuckDBMcpTaskStore(
        this IServiceCollection services,
        string connectionString,
        DuckDBMcpTaskStoreOptions? options = null,
        ResiliencePipelineBuilder? pipelineBuilder = null
    ) {
        services.TryAddSingleton<IMcpTaskStore>(sp => {
            var resolved = options ?? sp.GetService<DuckDBMcpTaskStoreOptions>();

            // Bind from configuration; a definite (non-null) IConfiguration keeps the Get<T> call
            // interceptable by the AOT-safe source-generated configuration binder.
            if (resolved is null && sp.GetService<IConfiguration>() is { } configuration)
                resolved = configuration.GetSection(DuckDBMcpTaskStoreOptions.ConfigurationSection).Get<DuckDBMcpTaskStoreOptions>();

            return new DuckDBMcpTaskStore(connectionString, resolved, sp.GetService<TimeProvider>(), pipelineBuilder);
        });

        return services;
    }
}
