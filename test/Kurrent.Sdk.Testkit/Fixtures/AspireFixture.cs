using System.Diagnostics;
using Humanizer;
using Aspire.Hosting.KurrentDB;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TUnit.Core.Interfaces;

namespace Kurrent.Testkit.Aspire;

/// <summary>
/// Base fixture that manages the lifecycle of an Aspire distributed application for testing.
/// Subclass <see cref="AspireFixture"/> (inline resource registration) or
/// <see cref="AspireFixture{T}"/> (AppHost project reference) — they differ only in how the
/// <see cref="IDistributedApplicationTestingBuilder"/> is created.
/// </summary>
public abstract class AspireFixtureBase : IAsyncInitializer, IAsyncDisposable {
    DistributedApplication? _app;

    public AspireFixtureBase() {
        ApplicationName  = GetType().Name.Replace("Fixture", "");
        Options          = new();
        ConfigureAppHost = (_, __) => { };
        ConfigureBuilder = _ => { };
        OnStarted        = () => ValueTask.CompletedTask;
        OnStopping       = () => ValueTask.CompletedTask;
    }
    
    protected string ApplicationName { get; private set; }

    protected AspireFixtureOptions Options { get; init; }

    /// <summary>
    /// Optional hook to configure the Aspire application host before it's built.
    /// This is where you can set options like disabling the dashboard, configuring logging, etc.
    /// </summary>
    protected Action<DistributedApplicationOptions, HostApplicationBuilderSettings> ConfigureAppHost { get; init; }

    /// <summary>
    /// Optional hook to allow further customization of the <see cref="DistributedApplicationTestingBuilder"/> before building the application.
    /// </summary>
    protected Action<IDistributedApplicationTestingBuilder> ConfigureBuilder { get; init; }

    /// <summary>
    /// Hook that runs after all resources are ready. Use for connection string resolution
    /// and client creation that depend on allocated endpoints.
    /// </summary>
    protected Func<ValueTask> OnStarted { get; init; }

    /// <summary>
    /// Final hook to allow custom teardown logic before the application is terminated.
    /// </summary>
    protected Func<ValueTask> OnStopping { get; init; }

    /// <summary>
    /// The Aspire distributed application instance managed by this fixture.
    /// </summary>
    public DistributedApplication App => _app ?? throw new("Fixture not initialized!");

    /// <summary>
    /// The Aspire application model containing all registered resources.
    /// </summary>
    public DistributedApplicationModel AppModel =>
        field ??= App.Services.GetRequiredService<DistributedApplicationModel>();

    public ILoggerFactory LoggerFactory => TestkitExecutionContext.LoggerFactory;

    public ILogger Logger => TestkitExecutionContext.Logger;

    /// <summary>
    /// Creates the <see cref="IDistributedApplicationTestingBuilder"/>.
    /// Override in subclasses to control how the builder is created.
    /// </summary>
    protected abstract Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync(
        Action<DistributedApplicationOptions, HostApplicationBuilderSettings> configure);

    async Task IAsyncInitializer.InitializeAsync() {
        var stopwatch = Stopwatch.StartNew();

        Logger.LogDebug("Initializing fixture...");

        var builder = await CreateBuilderAsync((options, settings) => {
            settings.ApplicationName      = ApplicationName;
            options.EnableResourceLogging = true;
            options.DisableDashboard      = true;
            
            settings.Configuration ??= new();
            settings.Configuration.AddConfiguration(TestkitExecutionContext.Configuration);
            
            try {
                ConfigureAppHost(options, settings);
                ApplicationName = settings.ApplicationName;
            } catch (Exception ex) {
                throw new("ConfigureAppHost failed", ex);
            }
        }).ConfigureAwait(false);

        builder.Services.AddSingleton(TestkitExecutionContext.LoggerFactory);
        
        try {
            ConfigureBuilder(builder);
        } catch (Exception ex) {
            throw new("ConfigureBuilder failed", ex);
        }

        using var cts = new CancellationTokenSource(Options.StartupTimeout);

        _app = await builder.BuildAsync(cts.Token).ConfigureAwait(false);

        Logger.LogInformation("Aspire app built in {Elapsed}", stopwatch.Elapsed.Humanize());
        
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
                throw new("Failed to run post-start setup", ex);
            }
        } catch (OperationCanceledException) when (cts.IsCancellationRequested) {
            throw new TimeoutException(
                $"Timed out after {Options.StartupTimeout.Humanize()} waiting for application startup. "
              + $"Check resource logs under '{ApplicationName}.Resources.*' for details.");
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync() {
        if (_app is null) return;
        
        try {
            await OnStopping().ConfigureAwait(false);
        } catch (Exception ex) {
            throw new("OnStopping failed", ex);
        }

        await App.StopAsync().ConfigureAwait(false);
        Logger.LogDebug("Aspire app stopped");
        await App.DisposeAsync().ConfigureAwait(false);
        Logger.LogDebug("Fixture disposed");
    }
}

/// <summary>
/// Aspire fixture that creates resources inline via <see cref="AspireFixtureBase.ConfigureBuilder"/>.
/// No AppHost project reference needed.
/// </summary>
public abstract class AspireFixture : AspireFixtureBase {
    protected override Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync(
        Action<DistributedApplicationOptions, HostApplicationBuilderSettings> configure
    ) => Task.FromResult(DistributedApplicationTestingBuilder.Create([], configure));
}

/// <summary>
/// Aspire fixture that loads resources from an AppHost project via <typeparamref name="T"/>.
/// </summary>
public abstract class AspireFixture<T> : AspireFixtureBase {
    protected override Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync(
        Action<DistributedApplicationOptions, HostApplicationBuilderSettings> configure
    ) => DistributedApplicationTestingBuilder.CreateAsync<T>([], configure);
}

public sealed record AspireFixtureOptions {
    /// <summary>
    /// Maximum time allowed for the application to start and all resources to become ready.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// How to wait for resources.
    /// </summary>
    public ResourceWaitBehavior WaitBehavior { get; init; } = ResourceWaitBehavior.AllHealthy;

    /// <summary>
    /// Predicate to determine which resources should be waited for.
    /// Defaults to all compute resources.
    /// </summary>
    public Func<IResource, bool> ShouldWaitForResource { get; init; } = r => r is IComputeResource;
}