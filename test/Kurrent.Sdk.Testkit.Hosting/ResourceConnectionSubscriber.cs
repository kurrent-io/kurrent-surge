using System.Collections.Concurrent;
using System.Collections.Frozen;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kurrent.Sdk.Testkit.Hosting;

/// <summary>
/// Discovers all <see cref="IResourceWithConnectionString"/> resources at startup,
/// and resolves their connection strings.
/// </summary>
public sealed partial class ResourceConnectionSubscriber(ILogger<ResourceConnectionSubscriber> logger) : IDistributedApplicationEventingSubscriber {
    static readonly TimeSpan DefaultRetrievalTimeout = TimeSpan.FromSeconds(60);
    
    readonly TaskCompletionSource<ConnectionStringLookup> _ready = new();

    public Task SubscribeAsync(
        IDistributedApplicationEventing eventing,
        DistributedApplicationExecutionContext executionContext,
        CancellationToken cancellationToken
    ) {
        // eventing.Subscribe<ResourceEndpointsAllocatedEvent>((@event, ct) => {
        //     logger.LogWarning("2. {Resource} ResourceEndpointsAllocatedEvent", @event.Resource.Name);
        //     return Task.CompletedTask;
        // });
        
        LogRetrievalInitiated(logger);
        
        eventing.Subscribe<AfterResourcesCreatedEvent>(async (evt, ct) => {

            var resources = evt.Services.GetRequiredService<DistributedApplicationModel>()
                .Resources.OfType<IResourceWithConnectionString>()
                .ToList();
            
            LogResourcesFound(logger, resources.Count, resources.Select(r => r.Name).Aggregate((a, b) => $"{a}, {b}"));
            
            var connectionStrings = new Dictionary<string, string>(resources.Count);
            var exceptions        = new List<Exception>(resources.Count);
            
            foreach (var resource in resources) {
                using var cts = CancellationTokenSource.CreateShortLived(DefaultRetrievalTimeout, ct);

                try {
                    var cn = await resource
                        .GetRequiredConnectionString(cts.Token)
                        .ConfigureAwait(false);

                    logger.LogWarning("Connection string: {ConnectionString}", cn);

                    connectionStrings[resource.Name] = cn;
                    
                    LogConnectionStringRetrieved(logger, resource.Name);
                } catch (OperationCanceledException) {
                    LogConnectionStringRetrievalCancelled(logger, resource.Name);
                    break;
                } catch (Exception ex) {
                    if (ex is TimeoutException)
                        LogConnectionStringRetrievalTimedOut(logger, resource.Name, DefaultRetrievalTimeout);
                    else
                        LogConnectionStringRetrievalFailed(logger, resource.Name, ex);

                    exceptions.Add(ex);
                }
            }

            if (exceptions.Count == 0) {
                LogRetrievalCompleted(logger);
                _ready.SetResult(new(connectionStrings.ToFrozenDictionary()));
            } else {
                _ready.SetException(exceptions);
                
                // executionContext.ServiceProvider
                //     .GetRequiredService<IHostApplicationLifetime>()
                //     .StopApplication();
            }
        });

        return Task.CompletedTask;
    }

    public async ValueTask<string> GetConnectionStringAsync(string resourceName) {
        logger.LogWarning("Getting connection string for resource: {ResourceName}", resourceName);
        return (await _ready.Task).Lookup(resourceName);
    }

    class ConnectionStringLookup(FrozenDictionary<string, string> connectionStrings) {
        public string Lookup(string resourceName) =>
            !connectionStrings.TryGetValue(resourceName, out var cs)
                ? throw new InvalidOperationException($"{resourceName} connection string not found")
                : cs;
    }

    #region ->> Logging <<-
    
    [LoggerMessage(LogLevel.Debug, "Connection string retrieval initiated")]
    static partial void LogRetrievalInitiated(ILogger<ResourceConnectionSubscriber> logger);

    [LoggerMessage(LogLevel.Information, "{ResourceCount} resources found with connection strings: {Resources}")]
    static partial void LogResourcesFound(ILogger<ResourceConnectionSubscriber> logger, int resourceCount, string resources);

    [LoggerMessage(LogLevel.Information, "{ResourceName} connection string retrieved")]
    static partial void LogConnectionStringRetrieved(ILogger<ResourceConnectionSubscriber> logger, string resourceName);

    [LoggerMessage(LogLevel.Error, "{ResourceName} connection string retrieval cancelled")]
    static partial void LogConnectionStringRetrievalCancelled(ILogger<ResourceConnectionSubscriber> logger, string resourceName);

    [LoggerMessage(LogLevel.Error, "{ResourceName} connection string retrieval timed out after {Timeout}")]
    static partial void LogConnectionStringRetrievalTimedOut(ILogger<ResourceConnectionSubscriber> logger, string resourceName, TimeSpan timeout);

    [LoggerMessage(LogLevel.Error, "{ResourceName} connection string retrieval failed")]
    static partial void LogConnectionStringRetrievalFailed(ILogger<ResourceConnectionSubscriber> logger, string resourceName, Exception exception);

    [LoggerMessage(LogLevel.Debug, "Connection string retrieval completed")]
    static partial void LogRetrievalCompleted(ILogger<ResourceConnectionSubscriber> logger);
    
    #endregion
}



//
// /// <summary>
// /// Discovers all <see cref="IResourceWithConnectionString"/> resources at startup,
// /// resolves their connection strings, and caches them by name.
// /// <para>
// /// Register with: <c>app.Services.AddEventingSubscriber&lt;ResourceConnectionSubscriber&gt;();</c>
// /// </para>
// /// </summary>
// public sealed class ResourceConnectionSubscriberWorks : IDistributedApplicationEventingSubscriber {
//     readonly ConcurrentDictionary<string, string> _connectionStrings = new();
//     readonly TaskCompletionSource _ready = new();
//
//     public Task SubscribeAsync(
//         IDistributedApplicationEventing eventing,
//         DistributedApplicationExecutionContext executionContext,
//         CancellationToken cancellationToken
//     ) {
//         eventing.Subscribe<BeforeStartEvent>(async (@event, ct) => {
//             var model = @event.Services.GetRequiredService<DistributedApplicationModel>();
//
//             var tasks = model.Resources
//                 .OfType<IResourceWithConnectionString>()
//                 .Select(async resource => {
//                     var cs = await resource.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false);
//                     if (cs is not null)
//                         _connectionStrings.TryAdd(resource.Name, cs);
//                 });
//
//             await Task.WhenAll(tasks).ConfigureAwait(false);
//             _ready.TrySetResult();
//         });
//
//         return Task.CompletedTask;
//     }
//
//     public async Task<string> GetConnectionStringAsync(string resourceName, CancellationToken ct) {
//         await _ready.Task.WaitAsync(ct).ConfigureAwait(false);
//
//         return _connectionStrings.TryGetValue(resourceName, out var cs)
//             ? cs
//             : throw new InvalidOperationException($"Connection string for '{resourceName}' not found.");
//     }
//
//     public async Task<string> GetConnectionStringAsync(string resourceName, TimeSpan timeout) {
//         using var cts = new CancellationTokenSource(timeout);
//         return await GetConnectionStringAsync(resourceName, cts.Token).ConfigureAwait(false);
//     }
//
//     // public string GetConnectionString(string resourceName) =>
//     //     _connectionStrings.TryGetValue(resourceName, out var cs)
//     //         ? cs
//     //         : throw new InvalidOperationException($"Connection string for '{resourceName}' is not yet available.");
// }



//
// /// <summary>
// /// Discovers all <see cref="IResourceWithConnectionString"/> resources at startup,
// /// and resolves their connection strings.
// /// </summary>
// public sealed class ResourceConnectionProvider(bool stopAppOnError = true) : IDistributedApplicationEventingSubscriber {
//     readonly TaskCompletionSource _ready = new();
//
//     readonly Dictionary<string, string> _connectionStrings = new();
//
//     public Task SubscribeAsync(
//         IDistributedApplicationEventing eventing,
//         DistributedApplicationExecutionContext executionContext,
//         CancellationToken cancellationToken
//     ) {
//         var logger      = executionContext.ServiceProvider.GetRequiredService<ILogger<ResourceConnectionProvider>>();
//         var appLifetime = executionContext.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();
//         
//         logger.LogDebug("Connection string retrieval initiated");
//
//         var resources = executionContext.ServiceProvider.GetRequiredService<DistributedApplicationModel>()
//             .Resources.OfType<IResourceWithConnectionString>()
//             .ToList();
//             
//         logger.LogInformation(
//             "{ResourceCount} resources found with connection strings: {Resources}", 
//             resources.Count, resources.Select(r => r.Name).Aggregate((a, b) => $"{a}, {b}"));
//         
//         eventing.Subscribe<BeforeStartEvent>(async (evt, ct) => {
//             // var resources = evt.Services.GetRequiredService<DistributedApplicationModel>()
//             //     .Resources.OfType<IResourceWithConnectionString>()
//             //     .ToList();
//             //
//             // logger.LogInformation(
//             //     "Found {ResourceCount} resources with connection strings: {Resources}", 
//             //     resources.Count, resources.Select(r => r.Name).Aggregate((a, b) => $"{a}, {b}"));
//
//             foreach (var resource in resources) {
//                 try {
//                     _connectionStrings[resource.Name] = await resource
//                         .GetRequiredConnectionString(ct).ConfigureAwait(false);
//                     
//                     logger.LogInformation("{ResourceName} connection string retrieved", resource.Name);
//                 } catch (Exception ex) {
//                     logger.LogError(ex, "{ResourceName} connection string retrieval failed", resource.Name);
//
//                     if (stopAppOnError)
//                         appLifetime.StopApplication();
//                 }
//             }
//             
//             logger.LogDebug("Connection string retrieval completed");
//             _ready.SetResult();
//         });
//
//         return Task.CompletedTask;
//     }
//
//     public async Task<string> GetConnectionStringAsync(string resourceName) {
//         await _ready.Task;
//         
//         return _connectionStrings.TryGetValue(resourceName, out var connString)
//             ? connString
//             : throw new InvalidOperationException($"{resourceName} connection string not found");
//     }
// }


//
// public sealed class ResourceConnectionSubscriber3() : IDistributedApplicationEventingSubscriber {
//     readonly Dictionary<string, ReferenceExpression> _expressions = new();
//
//     public Task SubscribeAsync(
//         IDistributedApplicationEventing eventing,
//         DistributedApplicationExecutionContext executionContext,
//         CancellationToken cancellationToken
//     ) {
//         eventing.Subscribe<BeforeStartEvent>(async (evt, ct) => {
//             var logger = evt.Services.GetRequiredService<ILogger<ResourceConnectionSubscriber3>>();
//             
//             logger.LogInformation("Subscribing to resource connection events");
//             
//             var resources = evt.Services.GetRequiredService<DistributedApplicationModel>()
//                 .Resources.OfType<IResourceWithConnectionString>()
//                 .ToAsyncEnumerable();
//             
//             await foreach (var resource in resources.WithCancellation(ct)) {
//                 _expressions.Add(resource.Name, resource.ConnectionStringExpression);
//                 logger.LogInformation("Resource connection string tracked: {ResourceName}", resource.Name);
//             }
//         });
//
//         return Task.CompletedTask;
//     }
//
//     public async Task<string> GetConnectionStringAsync(string resourceName, CancellationToken ct) {
//         if (!_expressions.TryGetValue(resourceName, out var expression))
//             throw new InvalidOperationException($"Resource '{resourceName}' not found.");
//
//         var connString = await expression.GetValueAsync(ct).ConfigureAwait(false);  
//             
//         return connString 
//             ?? throw new InvalidOperationException($"Connection string for '{resourceName}' not found.");
//
//     }
//
//     public async Task<string> GetConnectionStringAsync(string resourceName, TimeSpan timeout) {
//         using var cts = new CancellationTokenSource(timeout);
//         return await GetConnectionStringAsync(resourceName, cts.Token).ConfigureAwait(false);
//     }
// }
//
// public sealed class ResourceConnectionSubscriber4() : IDistributedApplicationEventingSubscriber {
//     readonly Dictionary<string, TaskCompletionSource<string>> _tasks = new();
//
//     public Task SubscribeAsync(
//         IDistributedApplicationEventing eventing,
//         DistributedApplicationExecutionContext executionContext,
//         CancellationToken cancellationToken
//     ) {
//         eventing.Subscribe<BeforeStartEvent>(async (evt, ct) => {
//             var logger = evt.Services.GetRequiredService<ILogger<ResourceConnectionSubscriber3>>();
//             
//             logger.LogInformation("Subscribing to resource connection events");
//
//             await evt
//                 .Services.GetRequiredService<DistributedApplicationModel>()
//                 .Resources.OfType<IResourceWithConnectionString>()
//                 .ToAsyncEnumerable()
//                 .ToDictionaryAsync(
//                     (r, _) => ValueTask.FromResult(r.Name), 
//                     async (r, _) => {
//                         var tcs = new TaskCompletionSource<string>();
//
//                         try {
//                             var connString = await r.GetRequiredConnectionString(ct);
//                             logger.LogInformation("Resource {ResourceName} connection string retrieved", r.Name);
//                             tcs.SetResult(connString);
//                         } catch (Exception ex) {
//                             logger.LogError(ex, "Resource {ResourceName} connection string retrieval failed", r.Name);
//                             tcs.SetException(ex);
//                         }
//                         return tcs;
//                     }, cancellationToken: ct);
//         });
//         
//         return Task.CompletedTask;
//     }
//
//     public async Task<string> GetConnectionStringAsync(string resourceName, CancellationToken ct) {
//         if (!_tasks.TryGetValue(resourceName, out var tcs))
//             throw new InvalidOperationException($"Resource '{resourceName}' not found.");
//
//         return await tcs.Task.ConfigureAwait(false); 
//     }
//     
//     public async Task<string> GetConnectionStringAsync(string resourceName, TimeSpan timeout) {
//         using var cts = new CancellationTokenSource(timeout);
//         return await GetConnectionStringAsync(resourceName, cts.Token).ConfigureAwait(false);
//     }
// }
//
// public interface IResourceConnectionSubscriber : IDistributedApplicationEventingSubscriber {
//     string GetConnectionStringAsync(string resourceName, CancellationToken ct);
//     
//     public async Task<string> GetConnectionStringAsync(string resourceName, TimeSpan timeout) {
//         using var cts = new CancellationTokenSource(timeout);
//         return await GetConnectionStringAsync(resourceName, cts.Token).ConfigureAwait(false);
//     }
// }
