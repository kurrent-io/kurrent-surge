using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
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
public abstract class AspireFixtureBase : IAsyncInitializer, IAsyncDisposable, ITestEndEventReceiver {
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
            settings.ApplicationName = ApplicationName;
            options.DisableDashboard = true;
            
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

        // Subscribe to resource logs BEFORE StartAsync so we're the first subscriber.
        // DCP only streams container stdout/stderr when subscribers exist, and the
        // testing framework's ResourceLoggerForwarderService (a hosted service) starts
        // during StartAsync. By subscribing first we capture the full log stream.
        StartResourceLogSubscriptions();

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
            var logs = FormatResourceLogs();

            throw new TimeoutException(
                $"Timed out after {Options.StartupTimeout.Humanize()} waiting for application startup.{logs}");
        }
    }

    #region ->> Resource Log Collection <<-

    readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _resourceLogs = new();
    CancellationTokenSource? _logSubscriptionCts;

    /// <summary>
    /// Maximum number of log lines to surface per resource when dumping on failure.
    /// </summary>
    protected int MaxLogLinesPerResource { get; init; } = 50;

    /// <summary>
    /// Watches <see cref="ResourceNotificationService"/> for new resources and subscribes
    /// to their log streams via <see cref="ResourceLoggerService"/>. This mirrors how
    /// Aspire's internal <c>ResourceLoggerForwarderService</c> works — it discovers
    /// resources by their runtime ID (which may differ from the model name) and subscribes
    /// to their log streams as they appear.
    /// </summary>
    void StartResourceLogSubscriptions() {
        _logSubscriptionCts = new CancellationTokenSource();
        _ = WatchAndBufferResourceLogsAsync(_logSubscriptionCts.Token);
    }

    async Task WatchAndBufferResourceLogsAsync(CancellationToken ct) {
        try {
            var notificationService = App.Services.GetRequiredService<ResourceNotificationService>();
            var loggerService = App.Services.GetRequiredService<ResourceLoggerService>();
            var subscribedIds = new HashSet<string>();

            await foreach (var evt in notificationService.WatchAsync(ct).ConfigureAwait(false)) {
                if (subscribedIds.Add(evt.ResourceId)) {
                    var queue = _resourceLogs.GetOrAdd(evt.Resource.Name, _ => new());
                    _ = BufferLogsAsync(loggerService, evt.ResourceId, queue, ct);
                }
            }
        } catch (OperationCanceledException) { }
    }

    static async Task BufferLogsAsync(
        ResourceLoggerService loggerService,
        string resourceId,
        ConcurrentQueue<string> queue,
        CancellationToken ct
    ) {
        try {
            await foreach (var batch in loggerService.WatchAsync(resourceId)
                .WithCancellation(ct).ConfigureAwait(false)) {
                foreach (var line in batch)
                    queue.Enqueue(line.Content);
            }
        } catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Called by TUnit after each test completes. When a test fails, dumps the buffered
    /// resource logs to the fixture's logger for diagnostic visibility.
    /// </summary>
    async ValueTask ITestEndEventReceiver.OnTestEnd(TestContext context) {
        if (context.Execution.Result?.State is not (TestState.Failed or TestState.Timeout))
            return;

        if (_app is null)
            return;

        var logs = FormatResourceLogs();

        if (logs.Length > 0) {
            Logger.LogError(
                "Test {TestName} {State} — resource logs:{Logs}",
                context.Metadata.TestDetails.TestName,
                context.Execution.Result.State,
                logs);
        }

        await ValueTask.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Formats the buffered resource logs for all compute resources into a single
    /// string block suitable for embedding in log messages or exceptions.
    /// </summary>
    protected string FormatResourceLogs() {
        var sb = new StringBuilder();

        foreach (var (name, queue) in _resourceLogs) {
            var lines = queue.ToArray();
            if (lines.Length == 0) continue;

            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine($"--- {name} logs ---");

            if (lines.Length > MaxLogLinesPerResource) {
                var skipped = lines.Length - MaxLogLinesPerResource;
                sb.AppendLine($"  ... ({skipped} earlier lines omitted)");
                foreach (var line in lines.Skip(skipped))
                    sb.AppendLine($"  {line}");
            } else {
                foreach (var line in lines)
                    sb.AppendLine($"  {line}");
            }
        }

        return sb.ToString();
    }

    #endregion // Resource Log Collection

    async ValueTask IAsyncDisposable.DisposeAsync() {
        if (_app is null) return;

        if (_logSubscriptionCts is not null) {
            await _logSubscriptionCts.CancelAsync().ConfigureAwait(false);
            _logSubscriptionCts.Dispose();
        }

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