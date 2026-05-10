using System.Diagnostics;
using Aspire.Hosting.Testing;
using Humanizer;
using Kurrent.Sdk.Testkit.Hosting;
using Kurrent.Testing.Testkit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TUnit.Core.Interfaces;

namespace Kurrent.Sdk.Testkit.Fixtures;


/// <summary>
/// A fixture that manages the lifecycle of an Aspire distributed application for testing.
/// Implements <see cref="IAsyncInitializer"/> and <see cref="IAsyncDisposable"/> to integrate
/// with TUnit's lifecycle automatically.
/// </summary>
/// <remarks>
/// <para>
/// Use directly with <c>[ClassDataSource&lt;AspireFixture&lt;Projects.MyAppHost&gt;&gt;(Shared = SharedType.PerTestSession)]</c>
/// or subclass to customize behavior via the virtual configuration hooks.
/// </para>
/// </remarks>
public abstract class AspireFixture : IAsyncInitializer, IAsyncDisposable {
    protected virtual string ApplicationName => field ??= GetType().Name;

    protected AspireFixtureOptions Options { get; init; } = new();

    /// <summary>
    /// Optional hook to configure the Aspire application host before it's built.
    /// This is where you can set options like disabling the dashboard, configuring logging, etc.
    /// </summary>
    protected Action<DistributedApplicationOptions, HostApplicationBuilderSettings> ConfigureAppHost { get; init; } = (__, ___) => { };

    /// <summary>
    /// Optional hook to allow further customization of the <see cref="DistributedApplicationTestingBuilder"/> before building the application.
    /// </summary>
    protected Action<IDistributedApplicationTestingBuilder> ConfigureBuilder { get; init; } = __ => { };

    /// <summary>
    /// Final hook to allow custom setup logic after the application is built but before it starts.
    /// </summary>
    protected Func<ValueTask> OnSetup { get; init; } = () => ValueTask.CompletedTask;

    /// <summary>
    /// Hook that runs after all resources are ready. Use for connection string resolution
    /// and client creation that depend on allocated endpoints.
    /// </summary>
    protected Func<ValueTask> OnStarted { get; init; } = () => ValueTask.CompletedTask;

    /// <summary>
    /// Final hook to allow custom teardown logic before the application is terminated.
    /// </summary>
    protected Func<ValueTask> OnTeardown { get; init; } = () => ValueTask.CompletedTask;

    /// <summary>
    /// The Aspire distributed application instance managed by this fixture.
    /// </summary>
    /// <remarks>
    /// Tests can use this property to interact with the application, query resource states, collect logs, etc.
    /// </remarks>
    public DistributedApplication App { get; private set; } = null!;

    /// <summary>
    /// The Aspire application model containing all registered resources.
    /// </summary>
    public DistributedApplicationModel AppModel =>
        field ??= App.Services.GetRequiredService<DistributedApplicationModel>();

    /// <summary>
    /// The collection of resources registered in the application.
    /// </summary>
    public IResourceCollection Resources => AppModel.Resources;
    
    public ILoggerFactory LoggerFactory => TestkitExecutionContext.LoggerFactory;

    public ILogger Logger => field ??= TestkitExecutionContext.Logger; //Factory.CreateLogger(ApplicationName);
    
    /// <summary>
    /// The <see cref="FakeTimeProvider"/> instance that allows tests to manipulate
    /// time within the Aspire application.
    /// </summary>
    public FakeTimeProvider Time { get; private set; } = null!;
    
    async Task IAsyncInitializer.InitializeAsync() {
        var stopwatch = Stopwatch.StartNew();

        Logger.LogDebug("Initializing fixture...");

        // ReSharper disable once MethodHasAsyncOverload
        var builder = DistributedApplicationTestingBuilder.Create([], (options, settings) => {
            // Make the application name available in the AppHost's configuration
            // and settings for logging, metrics, etc.
            settings.ApplicationName = ApplicationName;
            
            // Enable resource logging for better diagnostics on startup issues
            options.EnableResourceLogging = false; 
            
            // disable the dashboard since we use the aspire cli
            options.DisableDashboard = true;
            
            // All custom configuration hooks run after these defaults are applied,
            // so they can override as needed
            try {
                ConfigureAppHost(options, settings);
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to configure aspire app host");
                throw;
            }
        });
        
        builder.Services.AddSingleton(TestkitExecutionContext.LoggerFactory);
        
        builder.Services
            .AddSingleton(Time = new(DateTimeOffset.UtcNow))
            .AddSingleton<TimeProvider>(sp => sp.GetRequiredService<FakeTimeProvider>());
        
        // allow tests to further customize the builder (e.g. register test services)
        // in a safe way with error handling
        try {
            ConfigureBuilder(builder);
        } catch (Exception ex) {
            Logger.LogError(ex, "Failed to configure aspire app builder");
            throw;
        }
        
        using var cts = new CancellationTokenSource(Options.StartupTimeout);
        
        App = await builder.BuildAsync(cts.Token).ConfigureAwait(false);

        Logger.LogInformation("Aspire app built in {Elapsed}", stopwatch.Elapsed.Humanize());

        try {
            await OnSetup().ConfigureAwait(false);
        } catch (Exception ex) {
            Logger.LogError(ex, "Failed to setup fixture");
            throw;
        }

        Logger.LogInformation("Starting application with resources: {ResourceList}",
            string.Join(", ", AppModel.Resources.Select(r => r.Name)));

        try {
            await App.StartAsync(cts.Token).ConfigureAwait(false);
            
            Logger.LogInformation(
                "Aspire app started in {Elapsed}. Waiting for resources (behavior: {WaitBehavior})...",
                stopwatch.Elapsed.Humanize(), Options.WaitBehavior);

            await App
                .EnsureResourcesReadyAsync(Options.ShouldWaitForResource, Options.WaitBehavior, cts.Token)
                .ConfigureAwait(false);

            Logger.LogInformation("All resources ready in {Elapsed}", stopwatch.Elapsed.Humanize());

            try {
                await OnStarted().ConfigureAwait(false);
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to run post-start setup");
                throw;
            }
        } catch (OperationCanceledException) when (cts.IsCancellationRequested) {
            throw new TimeoutException(
                $"Timed out after {Options.StartupTimeout.Humanize()} waiting for application startup. "
              + $"Check resource logs under '{ApplicationName}.Resources.*' for details.");
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync() {
        if (App is not IAsyncDisposable disposable)
            return;

        Logger.LogTrace("Tearing down fixture...");

        try {
            await OnTeardown().ConfigureAwait(false);
        } catch (Exception ex) {
            Logger.LogError(ex, "Failed to teardown fixture");
            throw;
        }

        await App.StopAsync().ConfigureAwait(false);
        await disposable.DisposeAsync().ConfigureAwait(false);

        Logger.LogInformation("Fixture tear down complete");
    }

    /// <summary>
    /// Gets the connection string for the named resource.
    /// </summary>
    public ValueTask<string?> GetConnectionString(string resourceName, CancellationToken cancellationToken = default) =>
        App.GetConnectionStringAsync(resourceName, cancellationToken);
}

public sealed record AspireFixtureOptions {
    /// <summary>
    /// Maximum time allowed for the application to start and all resources to become ready.
    /// Default: 60 seconds.
    /// </summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Maximum time allowed for the application to shutdown.
    /// Default: 60 seconds.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(60);

    
    /// <summary>
    /// How to wait for resources.
    /// </summary>
    public ResourceWaitBehavior WaitBehavior { get; init; } = ResourceWaitBehavior.AllHealthy;

    /// <summary>
    /// Predicate to determine which resources should be waited for.
    /// Defaults to all compute resources that are not child resources.
    /// </summary>
    public Func<IResource, bool> ShouldWaitForResource { get; init; } = r => r is IComputeResource and not IResourceWithParent;
}

static class AspireExtensions {
    extension(IDistributedApplicationTestingBuilder builder) {
        /// <summary>
        /// Builds the application within the specified timeout.
        /// </summary>
        /// <param name="timeout"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public async Task BuildAsync(TimeSpan timeout) {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfEqual(timeout, Timeout.InfiniteTimeSpan);

            using var cts = new CancellationTokenSource(timeout);
            await builder.BuildAsync(cts.Token).ConfigureAwait(false);
        }
    }
}