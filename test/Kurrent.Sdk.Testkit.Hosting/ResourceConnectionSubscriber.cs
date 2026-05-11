using System.Collections.Concurrent;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kurrent.Sdk.Testkit.Hosting;

/// <summary>
/// Auto-discovers all <see cref="IResourceWithConnectionString"/> resources and resolves
/// their connection strings as they become available via <see cref="ConnectionStringAvailableEvent"/>.
/// <para>
/// Register with: <c>app.Services.AddEventingSubscriber&lt;ResourceConnectionSubscriberFixed&gt;();</c>
/// </para>
/// </summary>
public sealed partial class ResourceConnectionSubscriber(ILogger<ResourceConnectionSubscriber> logger) : IDistributedApplicationEventingSubscriber {
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

    public async Task<string> GetConnectionStringAsync(string resourceName, CancellationToken ct = default) {
        await _ready.Task.WaitAsync(ct).ConfigureAwait(false);
        return Lookup(resourceName);
    }

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
    static partial void LogRetrievalInitiated(ILogger<ResourceConnectionSubscriber> logger, int resourceCount, string resources);

    [LoggerMessage(LogLevel.Information, "{ResourceName} connection string retrieved")]
    static partial void LogConnectionStringRetrieved(ILogger<ResourceConnectionSubscriber> logger, string resourceName);

    [LoggerMessage(LogLevel.Error, "Connection string retrieval failed")]
    static partial void LogConnectionStringRetrievalFailed(ILogger<ResourceConnectionSubscriber> logger, string resourceName, Exception exception);

    [LoggerMessage(LogLevel.Debug, "Connection string retrieval completed")]
    static partial void LogRetrievalCompleted(ILogger<ResourceConnectionSubscriber> logger);

    #endregion
}