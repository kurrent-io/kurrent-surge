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
        /// <param name="filter">Predicate selecting which resources to wait for.</param>
        /// <param name="behavior">How to determine readiness.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task EnsureResourcesReadyAsync(
            Func<IResource, bool> filter,
            ResourceWaitBehavior behavior,
            CancellationToken ct
        ) {
            if (behavior == ResourceWaitBehavior.Unspecified) return;

            var model = app.Services.GetRequiredService<DistributedApplicationModel>();

            await Task.WhenAll(model.Resources
                .Where(filter)
                .Select(r => app.EnsureResourceReadyAsync(r.Name, behavior, ct)));
        }

        /// <summary>
        /// Ensures all resources matching <paramref name="filter"/> are ready within
        /// <paramref name="timeout"/>.
        /// </summary>
        /// <param name="filter">Predicate selecting which resources to wait for.</param>
        /// <param name="behavior">How to determine readiness.</param>
        /// <param name="timeout">Maximum time to wait. Must be positive and finite.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is negative or <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </exception>
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
        /// Ensures all compute resources are ready within <paramref name="timeout"/>.
        /// </summary>
        /// <param name="behavior">How to determine readiness.</param>
        /// <param name="timeout">Maximum time to wait. Must be positive and finite.</param>
        public Task EnsureResourcesReadyAsync(ResourceWaitBehavior behavior, TimeSpan timeout) =>
            app.EnsureResourcesReadyAsync(DefaultFilter, behavior, timeout);

        /// <summary>
        /// Ensures all compute resources are ready per the specified <paramref name="behavior"/>.
        /// </summary>
        /// <param name="behavior">How to determine readiness.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task EnsureResourcesReadyAsync(ResourceWaitBehavior behavior, CancellationToken ct) =>
            app.EnsureResourcesReadyAsync(DefaultFilter, behavior, ct);

        /// <summary>
        /// Ensures all compute resources are healthy, with built-in fail-fast when any
        /// resource enters <see cref="KnownResourceStates.FailedToStart"/>.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task EnsureResourcesReadyAsync(CancellationToken cancellationToken) =>
            app.EnsureResourcesReadyAsync(DefaultFilter, ResourceWaitBehavior.AllHealthy, cancellationToken);

        /// <summary>
        /// Ensures all compute resources are healthy within <paramref name="timeout"/>.
        /// </summary>
        /// <param name="timeout">Maximum time to wait. Must be positive and finite.</param>
        public Task EnsureResourcesReadyAsync(TimeSpan timeout) =>
            app.EnsureResourcesReadyAsync(DefaultFilter, ResourceWaitBehavior.AllHealthy, timeout);

        /// <summary>
        /// Ensures the named resources are ready per the specified <paramref name="behavior"/>.
        /// Resource name matching is case-insensitive.
        /// </summary>
        /// <param name="resources">Resource names to wait for.</param>
        /// <param name="behavior">How to determine readiness.</param>
        /// <param name="ct">Cancellation token.</param>
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
        /// Ensures the named resources are ready within <paramref name="timeout"/>.
        /// Resource name matching is case-insensitive.
        /// </summary>
        /// <param name="resources">Resource names to wait for.</param>
        /// <param name="behavior">How to determine readiness.</param>
        /// <param name="timeout">Maximum time to wait. Must be positive and finite.</param>
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
        /// <param name="resources">Resource names to wait for.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task EnsureResourcesReadyAsync(List<string> resources, CancellationToken ct) {
            ArgumentOutOfRangeException.ThrowIfZero(resources.Count);
            return app.EnsureResourcesReadyAsync(
                r => resources.Contains(r.Name, InvariantCultureIgnoreCase),
                ResourceWaitBehavior.AllHealthy, ct);
        }

        /// <summary>
        /// Ensures the named resources are healthy within <paramref name="timeout"/>.
        /// Resource name matching is case-insensitive.
        /// </summary>
        /// <param name="resources">Resource names to wait for.</param>
        /// <param name="timeout">Maximum time to wait. Must be positive and finite.</param>
        public Task EnsureResourcesReadyAsync(List<string> resources, TimeSpan timeout) {
            ArgumentOutOfRangeException.ThrowIfZero(resources.Count);
            return app.EnsureResourcesReadyAsync(
                r => resources.Contains(r.Name, InvariantCultureIgnoreCase),
                ResourceWaitBehavior.AllHealthy, timeout);
        }

        /// <summary>
        /// Ensures a single resource is ready per the specified <paramref name="behavior"/>.
        /// </summary>
        /// <param name="resource">The resource name.</param>
        /// <param name="behavior">How to determine readiness.</param>
        /// <param name="ct">Cancellation token.</param>
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
        /// Ensures a single resource is ready within <paramref name="timeout"/>.
        /// </summary>
        /// <param name="resource">The resource name.</param>
        /// <param name="behavior">How to determine readiness.</param>
        /// <param name="timeout">Maximum time to wait. Must be positive and finite.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is negative or <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </exception>
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
        /// <param name="resource">The resource name.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task EnsureResourceReadyAsync(string resource, CancellationToken ct) =>
            app.EnsureResourceReadyAsync(resource, ResourceWaitBehavior.AllHealthy, ct);

        /// <summary>
        /// Ensures a single resource is healthy within <paramref name="timeout"/>.
        /// </summary>
        /// <param name="resource">The resource name.</param>
        /// <param name="timeout">Maximum time to wait. Must be positive and finite.</param>
        public Task EnsureResourceReadyAsync(string resource, TimeSpan timeout) =>
            app.EnsureResourceReadyAsync(resource, ResourceWaitBehavior.AllHealthy, timeout);

        /// <summary>
        /// Starts a resource by executing the <see cref="KnownResourceCommands.StartCommand"/>.
        /// </summary>
        /// <param name="resource">The resource name.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task StartResourceAsync(string resource, CancellationToken ct) =>
            app.ResourceCommands.ExecuteCommandAsync(resource, KnownResourceCommands.StartCommand, ct);

        /// <summary>
        /// Starts a resource within <paramref name="timeout"/>.
        /// </summary>
        /// <param name="resource">The resource name.</param>
        /// <param name="timeout">Maximum time to wait. Must be positive and finite.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is negative or <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        public async Task StartResourceAsync(string resource, TimeSpan timeout) {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfEqual(timeout, Timeout.InfiniteTimeSpan);

            using var cts = new CancellationTokenSource(timeout);
            await app.StartResourceAsync(resource, cts.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops a resource by executing the <see cref="KnownResourceCommands.StopCommand"/>.
        /// </summary>
        /// <param name="resource">The resource name.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task StopResourceAsync(string resource, CancellationToken ct) =>
            app.ResourceCommands.ExecuteCommandAsync(resource, KnownResourceCommands.StopCommand, ct);

        /// <summary>
        /// Stops a resource within <paramref name="timeout"/>.
        /// </summary>
        /// <param name="resource">The resource name.</param>
        /// <param name="timeout">Maximum time to wait. Must be positive and finite.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is negative or <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        public async Task StopResourceAsync(string resource, TimeSpan timeout) {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfEqual(timeout, Timeout.InfiniteTimeSpan);

            using var cts = new CancellationTokenSource(timeout);
            await app.StopResourceAsync(resource, cts.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Restarts a resource by executing the <see cref="KnownResourceCommands.RestartCommand"/>.
        /// </summary>
        /// <param name="resource">The resource name.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task RestartResourceAsync(string resource, CancellationToken ct) =>
            app.ResourceCommands.ExecuteCommandAsync(resource, KnownResourceCommands.RestartCommand, ct);

        /// <summary>
        /// Restarts a resource within <paramref name="timeout"/>.
        /// </summary>
        /// <param name="resource">The resource name.</param>
        /// <param name="timeout">Maximum time to wait. Must be positive and finite.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is negative or <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        public async Task RestartResourceAsync(string resource, TimeSpan timeout) {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfEqual(timeout, Timeout.InfiniteTimeSpan);

            using var cts = new CancellationTokenSource(timeout);
            await app.StopResourceAsync(resource, cts.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts the application within <paramref name="timeout"/>.
        /// </summary>
        /// <param name="timeout">Maximum time to wait. Must be positive and finite.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is negative or <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        public async Task StartAsync(TimeSpan timeout) {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfEqual(timeout, Timeout.InfiniteTimeSpan);

            using var cts = new CancellationTokenSource(timeout);
            await app.StartAsync(cts.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts the application and waits for all matching resources to become ready
        /// within a single timeout window.
        /// </summary>
        /// <param name="timeout">
        /// Maximum time for the full startup-to-ready sequence. Must be positive and finite.
        /// </param>
        /// <param name="behavior">
        /// How to determine readiness. Defaults to <see cref="ResourceWaitBehavior.AllHealthy"/>.
        /// </param>
        /// <param name="filter">
        /// Predicate selecting which resources to wait for. Defaults to all compute resources
        /// excluding child resources.
        /// </param>
        /// <param name="ct">
        /// Cancellation token for external cancellation (distinct from <paramref name="timeout"/>).
        /// </param>
        /// <exception cref="TimeoutException">
        /// The application did not start and become ready within <paramref name="timeout"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is negative or <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        public async Task LaunchAsync(
            TimeSpan timeout,
            ResourceWaitBehavior behavior = ResourceWaitBehavior.AllHealthy,
            Func<IResource, bool>? filter = null,
            CancellationToken ct = default
        ) {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfEqual(timeout, Timeout.InfiniteTimeSpan);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var timeoutToken = cts.Token;

            try {
                await app.StartAsync(timeoutToken).ConfigureAwait(false);
                await app.EnsureResourcesReadyAsync(filter ?? DefaultFilter, behavior, timeoutToken).ConfigureAwait(false);
            } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                // The linked CTS fired but the caller's token didn't — it's a timeout.
                throw new TimeoutException(
                    $"Application failed to start and become ready within {timeout.TotalSeconds:0}s.");
            }
            // If ct was cancelled, OperationCanceledException propagates as-is.
        }
    }

    extension(IDistributedApplicationBuilder app) {
        /// <summary>
        /// Waits for a named resource's connection string to become available.
        /// </summary>
        /// <param name="resourceName">The resource name to wait for.</param>
        /// <param name="timeout">Maximum time to wait.</param>
        /// <returns>The connection string.</returns>
        /// <exception cref="ApplicationException">
        /// The connection string was not available within <paramref name="timeout"/>.
        /// </exception>
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

        /// <summary>
        /// Waits for a typed resource's connection string to become available.
        /// </summary>
        /// <typeparam name="T">The resource type.</typeparam>
        /// <param name="resource">The resource instance.</param>
        /// <param name="timeout">Maximum time to wait.</param>
        /// <returns>The connection string.</returns>
        /// <exception cref="ApplicationException">
        /// The connection string was not available within <paramref name="timeout"/>.
        /// </exception>
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
    }

    extension(IResourceWithConnectionString resource) {
        /// <summary>
        /// Gets the connection string for this resource.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The connection string, or <c>null</c> if not available.</returns>
        public ValueTask<string?> GetConnectionString(CancellationToken ct = default) =>
            resource.ConnectionStringExpression.GetValueAsync(ct);

        /// <summary>
        /// Gets the connection string within <paramref name="timeout"/>.
        /// </summary>
        /// <param name="timeout">Maximum time to wait.</param>
        /// <returns>The connection string, or <c>null</c> if not available.</returns>
        public async ValueTask<string?> GetConnectionString(TimeSpan timeout) {
            using var cts = new CancellationTokenSource(timeout);
            return await resource.GetConnectionString(cts.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the connection string for this resource, throwing if not available.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The connection string.</returns>
        /// <exception cref="ApplicationException">The connection string is not available.</exception>
        public async ValueTask<string> GetRequiredConnectionString(CancellationToken ct = default) {
            var connString = await resource.GetConnectionString(ct);
            return connString ?? throw new ApplicationException($"Resource '{resource.Name}' connection string not available");
        }

        /// <summary>
        /// Gets the connection string within <paramref name="timeout"/>, throwing if not available.
        /// </summary>
        /// <param name="timeout">Maximum time to wait.</param>
        /// <returns>The connection string.</returns>
        /// <exception cref="ApplicationException">The connection string is not available.</exception>
        public async ValueTask<string> GetRequiredConnectionString(TimeSpan timeout) {
            using var cts = new CancellationTokenSource(timeout);
            return await resource.GetRequiredConnectionString(cts.Token).ConfigureAwait(false);
        }
    }

    extension(DistributedApplicationModel appModel) {
        /// <summary>
        /// Gets the connection string for a named resource of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The resource type implementing <see cref="IResourceWithConnectionString"/>.</typeparam>
        /// <param name="resourceName">The resource name.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The connection string, or <c>null</c> if not available.</returns>
        public ValueTask<string?> GetConnectionString<T>(string resourceName, CancellationToken ct = default) where T : IResourceWithConnectionString =>
            appModel.Resources.OfType<T>().Single(r => r.Name == resourceName).GetConnectionString(ct);

        /// <summary>
        /// Gets the connection string for a named resource of type <typeparamref name="T"/>
        /// within <paramref name="timeout"/>.
        /// </summary>
        /// <typeparam name="T">The resource type implementing <see cref="IResourceWithConnectionString"/>.</typeparam>
        /// <param name="resourceName">The resource name.</param>
        /// <param name="timeout">Maximum time to wait.</param>
        /// <returns>The connection string, or <c>null</c> if not available.</returns>
        public ValueTask<string?> GetConnectionString<T>(string resourceName, TimeSpan timeout) where T : IResourceWithConnectionString =>
            appModel.Resources.OfType<T>().Single(r => r.Name == resourceName).GetConnectionString(timeout);

        /// <summary>
        /// Gets the connection string for the default resource of type <typeparamref name="T"/>.
        /// The resource name is derived from the type name in lowercase.
        /// </summary>
        /// <typeparam name="T">The resource type implementing <see cref="IResourceWithConnectionString"/>.</typeparam>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The connection string, or <c>null</c> if not available.</returns>
        public ValueTask<string?> GetConnectionString<T>(CancellationToken ct = default) where T : IResourceWithConnectionString =>
            appModel.GetConnectionString<T>(typeof(T).Name.ToLowerInvariant(), ct);

        /// <summary>
        /// Gets the connection string for the default resource of type <typeparamref name="T"/>
        /// within <paramref name="timeout"/>.
        /// </summary>
        /// <typeparam name="T">The resource type implementing <see cref="IResourceWithConnectionString"/>.</typeparam>
        /// <param name="timeout">Maximum time to wait.</param>
        /// <returns>The connection string, or <c>null</c> if not available.</returns>
        public ValueTask<string?> GetConnectionString<T>(TimeSpan timeout) where T : IResourceWithConnectionString =>
            appModel.GetConnectionString<T>(typeof(T).Name.ToLowerInvariant(), timeout);

        /// <summary>
        /// Gets the connection string for a named resource of type <typeparamref name="T"/>,
        /// throwing if not available.
        /// </summary>
        /// <typeparam name="T">The resource type implementing <see cref="IResourceWithConnectionString"/>.</typeparam>
        /// <param name="resourceName">The resource name.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The connection string.</returns>
        /// <exception cref="ApplicationException">The connection string is not available.</exception>
        public ValueTask<string> GetRequiredConnectionString<T>(string resourceName, CancellationToken ct = default) where T : IResourceWithConnectionString =>
            appModel.Resources.OfType<T>().Single(r => r.Name == resourceName).GetRequiredConnectionString(ct);

        /// <summary>
        /// Gets the connection string for a named resource of type <typeparamref name="T"/>
        /// within <paramref name="timeout"/>, throwing if not available.
        /// </summary>
        /// <typeparam name="T">The resource type implementing <see cref="IResourceWithConnectionString"/>.</typeparam>
        /// <param name="resourceName">The resource name.</param>
        /// <param name="timeout">Maximum time to wait.</param>
        /// <returns>The connection string.</returns>
        /// <exception cref="ApplicationException">The connection string is not available.</exception>
        public ValueTask<string> GetRequiredConnectionString<T>(string resourceName, TimeSpan timeout) where T : IResourceWithConnectionString =>
            appModel.Resources.OfType<T>().Single(r => r.Name == resourceName).GetRequiredConnectionString(timeout);

        /// <summary>
        /// Gets the connection string for the default resource of type <typeparamref name="T"/>,
        /// throwing if not available. The resource name is derived from the type name in lowercase.
        /// </summary>
        /// <typeparam name="T">The resource type implementing <see cref="IResourceWithConnectionString"/>.</typeparam>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The connection string.</returns>
        /// <exception cref="ApplicationException">The connection string is not available.</exception>
        public ValueTask<string> GetRequiredConnectionString<T>(CancellationToken ct = default) where T : IResourceWithConnectionString =>
            appModel.GetRequiredConnectionString<T>(typeof(T).Name.ToLowerInvariant(), ct);

        /// <summary>
        /// Gets the connection string for the default resource of type <typeparamref name="T"/>
        /// within <paramref name="timeout"/>, throwing if not available. The resource name is
        /// derived from the type name in lowercase.
        /// </summary>
        /// <typeparam name="T">The resource type implementing <see cref="IResourceWithConnectionString"/>.</typeparam>
        /// <param name="timeout">Maximum time to wait.</param>
        /// <returns>The connection string.</returns>
        /// <exception cref="ApplicationException">The connection string is not available.</exception>
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
