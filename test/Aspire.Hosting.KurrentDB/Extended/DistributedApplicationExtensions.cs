using Kurrent.Client;
using Microsoft.Extensions.DependencyInjection;
using static System.StringComparer;

namespace Aspire.Hosting.KurrentDB;

public static class AspireExtensions {
    static readonly Func<IResource, bool> DefaultFilter = r => r is IComputeResource and not IResourceWithParent;

    extension(DistributedApplication app) {
        /// <summary>
        /// Ensures all resources matching <paramref name="filter"/> are ready per the specified
        /// behavior, with built-in fail-fast when any resource enters
        /// <see cref="KnownResourceStates.FailedToStart"/>.
        /// </summary>
        public async Task EnsureResourcesReadyAsync(
            Func<IResource, bool> filter,
            ResourceWaitBehavior behavior,
            CancellationToken ct
        ) {
            if (behavior == ResourceWaitBehavior.Unspecified) return;

            var model = app.Services.GetRequiredService<DistributedApplicationModel>();

            await model.Resources
                .Where(filter)
                .Select(r => app.EnsureResourceReadyAsync(r.Name, behavior, ct))
                .WhenAll();
        }
        
        /// <summary>
        /// Ensures all resources matching <paramref name="filter"/> are ready within the specified timeout.
        /// </summary>
        public async Task EnsureResourcesReadyAsync(
            Func<IResource, bool> filter,
            ResourceWaitBehavior behavior,
            TimeSpan timeout
        ) {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfEqual(timeout, Timeout.InfiniteTimeSpan);

            if (behavior == ResourceWaitBehavior.Unspecified) return;

            using var cts = new CancellationTokenSource(timeout);
            await app.EnsureResourcesReadyAsync(filter, behavior, cts.Token).ConfigureAwait(false);
        }
     
        /// <summary>
        /// Ensures all compute resources are ready within the specified timeout.
        /// </summary>
        public Task EnsureResourcesReadyAsync(ResourceWaitBehavior behavior, TimeSpan timeout) =>
            app.EnsureResourcesReadyAsync(DefaultFilter, behavior, timeout);

        /// <summary>
        /// Ensures all compute resources are ready per the specified behavior.
        /// </summary>
        public Task EnsureResourcesReadyAsync(ResourceWaitBehavior behavior, CancellationToken ct) =>
            app.EnsureResourcesReadyAsync(DefaultFilter, behavior, ct);
        
        /// <summary>
        /// Ensures all compute resources are healthy, with built-in fail-fast when any
        /// resource enters <see cref="KnownResourceStates.FailedToStart"/>.
        /// </summary>
        public Task EnsureResourcesReadyAsync(CancellationToken cancellationToken) =>
            app.EnsureResourcesReadyAsync(DefaultFilter, ResourceWaitBehavior.AllHealthy, cancellationToken);

        /// <summary>
        /// Ensures all compute resources are healthy within the specified timeout.
        /// </summary>
        public Task EnsureResourcesReadyAsync(TimeSpan timeout) =>
            app.EnsureResourcesReadyAsync(DefaultFilter, ResourceWaitBehavior.AllHealthy, timeout);

        /// <summary>
        /// Ensures the named resources are ready per the specified behavior.
        /// Resource name matching is case-insensitive.
        /// </summary>
        public Task EnsureResourcesReadyAsync(
            List<string> resources,
            ResourceWaitBehavior behavior,
            CancellationToken ct
        ) {
            ArgumentOutOfRangeException.ThrowIfZero(resources.Count);
            return app.EnsureResourcesReadyAsync(
                r => resources.Contains(r.Name, InvariantCultureIgnoreCase),
                behavior, ct);
        }

        /// <summary>
        /// Ensures the named resources are ready within the specified timeout.
        /// Resource name matching is case-insensitive.
        /// </summary>
        public Task EnsureResourcesReadyAsync(
            List<string> resources,
            ResourceWaitBehavior behavior,
            TimeSpan timeout
        ) {
            ArgumentOutOfRangeException.ThrowIfZero(resources.Count);
            return app.EnsureResourcesReadyAsync(
                r => resources.Contains(r.Name, InvariantCultureIgnoreCase),
                behavior, timeout);
        }

        /// <summary>
        /// Ensures the named resources are healthy.
        /// Resource name matching is case-insensitive.
        /// </summary>
        public Task EnsureResourcesReadyAsync(List<string> resources, CancellationToken ct) {
            ArgumentOutOfRangeException.ThrowIfZero(resources.Count);
            return app.EnsureResourcesReadyAsync(
                r => resources.Contains(r.Name, InvariantCultureIgnoreCase), 
                ResourceWaitBehavior.AllHealthy, ct);
        }

        /// <summary>
        /// Ensures the named resources are healthy within the specified timeout.
        /// Resource name matching is case-insensitive.
        /// </summary>
        public Task EnsureResourcesReadyAsync(List<string> resources, TimeSpan timeout) {
            ArgumentOutOfRangeException.ThrowIfZero(resources.Count);
            return app.EnsureResourcesReadyAsync(
                r => resources.Contains(r.Name, InvariantCultureIgnoreCase),
                ResourceWaitBehavior.AllHealthy, timeout);
        }

        /// <summary>
        /// Ensures a single resource is ready per the specified behavior.
        /// </summary>
        public async Task EnsureResourceReadyAsync(
            string resource,
            ResourceWaitBehavior behavior,
            CancellationToken ct
        ) {
            if (behavior == ResourceWaitBehavior.Unspecified) return;

            var svc = app.Services.GetRequiredService<ResourceNotificationService>();

            if (behavior == ResourceWaitBehavior.AllHealthy)
                await svc.WaitForResourceHealthyAsync(resource, WaitBehavior.StopOnResourceUnavailable, ct).ConfigureAwait(false);
            else
                await svc.WaitForResourceAsync(resource, KnownResourceStates.Running, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Ensures a single resource is ready within the specified timeout.
        /// </summary>
        public async Task EnsureResourceReadyAsync(
            string resource,
            ResourceWaitBehavior behavior,
            TimeSpan timeout
        ) {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfEqual(timeout, Timeout.InfiniteTimeSpan);

            using var cts = new CancellationTokenSource(timeout);
            await app.EnsureResourceReadyAsync(resource, behavior, cts.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Ensures a single resource is healthy, with built-in fail-fast when the
        /// resource enters <see cref="KnownResourceStates.FailedToStart"/>.
        /// </summary>
        public Task EnsureResourceReadyAsync(string resource, CancellationToken ct) =>
            app.EnsureResourceReadyAsync(resource, ResourceWaitBehavior.AllHealthy, ct);

        /// <summary>
        /// Ensures a single resource is healthy within the specified timeout.
        /// </summary>
        public Task EnsureResourceReadyAsync(string resource, TimeSpan timeout) =>
            app.EnsureResourceReadyAsync(resource, ResourceWaitBehavior.AllHealthy, timeout);

        /// <summary>
        /// Starts a resource by executing the <see cref="KnownResourceCommands.StartCommand"/>.
        /// </summary>
        public Task StartResourceAsync(string resource, CancellationToken ct) =>
            app.ResourceCommands.ExecuteCommandAsync(resource, KnownResourceCommands.StartCommand, ct);

        /// <summary>
        /// Starts a resource within the specified timeout.
        /// </summary>
        public async Task StartResourceAsync(string resource, TimeSpan timeout) {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfEqual(timeout, Timeout.InfiniteTimeSpan);

            using var cts = new CancellationTokenSource(timeout);
            await app.StartResourceAsync(resource, cts.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops a resource by executing the <see cref="KnownResourceCommands.StopCommand"/>.
        /// </summary>
        public Task StopResourceAsync(string resource, CancellationToken ct) =>
            app.ResourceCommands.ExecuteCommandAsync(resource, KnownResourceCommands.StopCommand, ct);

        /// <summary>
        /// Stops a resource within the specified timeout.
        /// </summary>
        public async Task StopResourceAsync(string resource, TimeSpan timeout) {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfEqual(timeout, Timeout.InfiniteTimeSpan);

            using var cts = new CancellationTokenSource(timeout);
            await app.StopResourceAsync(resource, cts.Token).ConfigureAwait(false);
        }
        
        /// <summary>
        /// Restarts a resource by executing the <see cref="KnownResourceCommands.RestartCommand"/>.
        /// </summary>
        public Task RestartResourceAsync(string resource, CancellationToken ct) =>
            app.ResourceCommands.ExecuteCommandAsync(resource, KnownResourceCommands.RestartCommand, ct);

        /// <summary>
        /// Restarts a resource within the specified timeout.
        /// </summary>
        public async Task RestartResourceAsync(string resource, TimeSpan timeout) {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfEqual(timeout, Timeout.InfiniteTimeSpan);

            using var cts = new CancellationTokenSource(timeout);
            await app.StopResourceAsync(resource, cts.Token).ConfigureAwait(false);
        }
        
        /// <summary>
        /// Starts the application within the specified timeout.
        /// </summary>
        public async Task StartAsync(TimeSpan timeout) {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfEqual(timeout, Timeout.InfiniteTimeSpan);

            using var cts = new CancellationTokenSource(timeout);
            await app.StartAsync(cts.Token).ConfigureAwait(false);
        }
    }

    extension(IDistributedApplicationBuilder app) {
        public ValueTask<string> WaitForResourceConnection(string resourceName, TimeSpan timeout) {
            string? connectionString = null;
           
            app.Eventing.Subscribe<ConnectionStringAvailableEvent>(async (evt, ct) => {
                if (evt.Resource.Name == resourceName && evt.Resource is IResourceWithConnectionString resource) {
                    connectionString = await resource.ConnectionStringExpression
                        .GetValueAsync(ct)
                        .ConfigureAwait(false);
                }
            });

            return ValueTask.FromResult(
                connectionString ?? throw new ApplicationException($"Resource '{resourceName}' connection string not available within timeout"));
        }
        
        public ValueTask<string> WaitForResourceConnection<T>(T resource, TimeSpan timeout) where T : IResourceWithConnectionString {
            string? connectionString = null;
           
            app.Eventing.Subscribe<ConnectionStringAvailableEvent>(resource, async (_, ct) => {
                connectionString = await resource.ConnectionStringExpression
                    .GetValueAsync(ct)
                    .ConfigureAwait(false);
            });

            return ValueTask.FromResult(
                connectionString ?? throw new ApplicationException($"Resource '{resource.Name}' connection string not available within timeout"));
        }
        
        // public async Task WaitForResourceConnection(string resourceName, TimeSpan timeout) {
        //     string? connectionString = null;
        //    
        //     using var cts = new CancellationTokenSource(timeout);
        //   
        //     var subscription = app.Eventing.Subscribe<ConnectionStringAvailableEvent>(async (evt, ct) => {
        //         if (evt.Resource.Name == resourceName && evt.Resource is IResourceWithConnectionString resource) {
        //             connectionString = await resource.ConnectionStringExpression
        //                 .GetValueAsync(ct)
        //                 .ConfigureAwait(false);
        //         }
        //     });
        //
        //     cts.UnsafeRegister(
        //         (state, ct) => {
        //             state.Eventing.Unsubscribe(state.subscription);
        //             return Task.CompletedTask;
        //         },
        //         (app.Eventing, subscription));
        // }
    }
    
    extension(IResourceWithConnectionString resource) {
        public ValueTask<string?> GetConnectionString(CancellationToken ct = default) =>
            resource.ConnectionStringExpression.GetValueAsync(ct);

        public async ValueTask<string?> GetConnectionString(TimeSpan timeout) {
            using var cts = new CancellationTokenSource(timeout);
            return await resource.GetConnectionString(cts.Token).ConfigureAwait(false);
        }
        
        public async ValueTask<string> GetRequiredConnectionString(CancellationToken ct = default) {
            var connString = await resource.GetConnectionString(ct);
            return connString ?? throw new ApplicationException($"Resource '{resource.Name}' connection string not available");
        }

        public async ValueTask<string> GetRequiredConnectionString(TimeSpan timeout) {
            using var cts = new CancellationTokenSource(timeout);
            return await resource.GetRequiredConnectionString(cts.Token).ConfigureAwait(false);
        }
    }
    
    extension( DistributedApplicationModel appModel) {
        public ValueTask<string?> GetConnectionString<T>(string resourceName, CancellationToken ct = default) where T : IResourceWithConnectionString =>
            appModel.Resources.OfType<T>().Single(r => r.Name == resourceName).GetConnectionString(ct);

        public ValueTask<string?> GetConnectionString<T>(string resourceName, TimeSpan timeout) where T : IResourceWithConnectionString =>
            appModel.Resources.OfType<T>().Single(r => r.Name == resourceName).GetConnectionString(timeout);
        
        public ValueTask<string?> GetConnectionString<T>(CancellationToken ct = default) where T : IResourceWithConnectionString =>
            appModel.GetConnectionString<T>(typeof(T).Name.ToLowerInvariant(), ct);

        public ValueTask<string?> GetConnectionString<T>(TimeSpan timeout) where T : IResourceWithConnectionString =>
            appModel.GetConnectionString<T>(typeof(T).Name.ToLowerInvariant(), timeout);
        
        public ValueTask<string> GetRequiredConnectionString<T>(string resourceName, CancellationToken ct = default) where T : IResourceWithConnectionString =>
            appModel.Resources.OfType<T>().Single(r => r.Name == resourceName).GetRequiredConnectionString(ct);

        public ValueTask<string> GetRequiredConnectionString<T>(string resourceName, TimeSpan timeout) where T : IResourceWithConnectionString =>
            appModel.Resources.OfType<T>().Single(r => r.Name == resourceName).GetRequiredConnectionString(timeout);
        
        public ValueTask<string> GetRequiredConnectionString<T>(CancellationToken ct = default) where T : IResourceWithConnectionString =>
            appModel.GetRequiredConnectionString<T>(typeof(T).Name.ToLowerInvariant(), ct);

        public ValueTask<string> GetRequiredConnectionString<T>(TimeSpan timeout) where T : IResourceWithConnectionString =>
            appModel.GetRequiredConnectionString<T>(typeof(T).Name.ToLowerInvariant(), timeout);
    }
}

/// <summary>
/// Specifies how to wait for resources during initialization.
/// </summary>
public enum ResourceWaitBehavior {
    /// <summary>
    /// Don't wait for any resources.
    /// </summary>
    Unspecified,
    
    /// <summary>
    /// Wait for all matching resources to pass health checks (default).
    /// </summary>
    AllHealthy,

    /// <summary>
    /// Wait for all matching resources to reach the Running state.
    /// </summary>
    AllRunning
}