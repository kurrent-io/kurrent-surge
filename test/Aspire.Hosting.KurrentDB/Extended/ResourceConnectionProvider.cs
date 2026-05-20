using System.Collections.Concurrent;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.KurrentDB;

/// <summary>
/// Auto-discovers all <see cref="IComputeResource"/> resources that implement
/// <see cref="IResourceWithConnectionString"/> and resolves their connection strings
/// as they become available via <see cref="ConnectionStringAvailableEvent"/>.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="IComputeResource"/> resources (containers, executables) are tracked.
/// Non-compute resources like <c>KurrentDBServerResource</c> implement
/// <see cref="IResourceWithConnectionString"/> but are never orchestrated by Aspire's DCP,
/// so their <see cref="ConnectionStringAvailableEvent"/> never fires.
/// Including them would cause <see cref="GetConnectionStringAsync(string, CancellationToken)"/>
/// to block indefinitely.
/// </para>
/// <para>
/// Register with <see cref="ResourceConnectionProviderExtensions.AddResourceConnectionProvider"/>.
/// </para>
/// </remarks>
sealed partial class ResourceConnectionProvider(ILogger<ResourceConnectionProvider> logger) : IDistributedApplicationEventingSubscriber {
    readonly ConcurrentDictionary<string, string> _connectionStrings = new();
    readonly TaskCompletionSource _ready = new();

    int _remaining;

    public Task SubscribeAsync(
        IDistributedApplicationEventing eventing,
        DistributedApplicationExecutionContext executionContext,
        CancellationToken cancellationToken
    ) {
        var resources = executionContext.ServiceProvider
            .GetRequiredService<DistributedApplicationModel>()
            .Resources.OfType<IResourceWithConnectionString>()
            .Where(r => r is IComputeResource)
            .ToList();

        _remaining = resources.Count;

        LogRetrievalInitiated(logger, _remaining,
            string.Join(", ", resources.Select(r => r.Name)));

        foreach (var resource in resources) {
            eventing.Subscribe<ConnectionStringAvailableEvent>(resource, async (_, ct) => {
                try {
                    _connectionStrings[resource.Name] = await resource
                        .GetRequiredConnectionString(ct)
                        .ConfigureAwait(false);
                    
                    LogConnectionStringRetrieved(logger, resource.Name);
                    
                    if (Interlocked.Decrement(ref _remaining) == 0) {
                        LogRetrievalCompleted(logger);
                        _ready.TrySetResult();
                    }
                } catch (Exception ex) {
                    LogConnectionStringRetrievalFailed(logger, resource.Name, ex);
                    _ready.TrySetException(ex);
                }
            });
        }

        if (_remaining == 0)
            _ready.TrySetResult();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the connection string for the named resource, waiting until all tracked
    /// resources have resolved their connection strings.
    /// </summary>
    /// <exception cref="InvalidOperationException">The resource name was not found among tracked resources.</exception>
    public async Task<string> GetConnectionStringAsync(string resourceName, CancellationToken ct = default) {
        await _ready.Task.WaitAsync(ct).ConfigureAwait(false);
        return Lookup(resourceName);
    }

    /// <inheritdoc cref="GetConnectionStringAsync(string, CancellationToken)"/>
    /// <exception cref="TimeoutException">All resources did not resolve within <paramref name="timeout"/>.</exception>
    public async Task<string> GetConnectionStringAsync(string resourceName, TimeSpan timeout) {
        await _ready.Task.WaitAsync(timeout).ConfigureAwait(false);
        return Lookup(resourceName);
    }
    
    string Lookup(string resourceName) =>
        _connectionStrings.TryGetValue(resourceName, out var cs)
            ? cs
            : throw new InvalidOperationException($"Connection string for '{resourceName}' not found.");

    #region ->> Logging <<-

    [LoggerMessage(LogLevel.Debug, "Connection string retrieval initiated for {ResourceCount} resources: {Resources}")]
    static partial void LogRetrievalInitiated(ILogger<ResourceConnectionProvider> logger, int resourceCount, string resources);

    [LoggerMessage(LogLevel.Information, "{ResourceName} connection string retrieved")]
    static partial void LogConnectionStringRetrieved(ILogger<ResourceConnectionProvider> logger, string resourceName);

    [LoggerMessage(LogLevel.Error, "Connection string retrieval failed")]
    static partial void LogConnectionStringRetrievalFailed(ILogger<ResourceConnectionProvider> logger, string resourceName, Exception exception);

    [LoggerMessage(LogLevel.Debug, "Connection string retrieval completed")]
    static partial void LogRetrievalCompleted(ILogger<ResourceConnectionProvider> logger);

    #endregion
}
