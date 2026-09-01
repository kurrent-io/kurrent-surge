using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    bool _initFailed;

    public AspireFixtureBase() {
        ApplicationName  = GetType().Name.Replace("Fixture", "");
        Options          = new();
        ConfigureAppHost = (_, __) => { };
        ConfigureBuilder = _ => { };
        BeforeStart      = () => ValueTask.CompletedTask;
        AfterStart       = () => ValueTask.CompletedTask;
        BeforeStop       = () => ValueTask.CompletedTask;
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
    /// Hook that runs after the app is built but before <see cref="DistributedApplication.StartAsync"/>.
    /// The <see cref="App"/> and its service provider are available but no containers are running yet.
    /// </summary>
    protected Func<ValueTask> BeforeStart { get; init; }

    /// <summary>
    /// Hook that runs after all resources are ready. Use for connection string resolution
    /// and client creation that depend on allocated endpoints.
    /// </summary>
    protected Func<ValueTask> AfterStart { get; init; }

    /// <summary>
    /// Hook that runs before the application is stopped. Use for client disposal and cleanup.
    /// </summary>
    protected Func<ValueTask> BeforeStop { get; init; }

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

        try {
            var builder = await CreateBuilderAsync((options, settings) => {
                settings.ApplicationName = ApplicationName;
                options.DisableDashboard = true;
                settings.Configuration ??= new();
                settings.Configuration.AddConfiguration(TestkitExecutionContext.Configuration);

                try {
                    ConfigureAppHost(options, settings);
                    ApplicationName = settings.ApplicationName;
                } catch (Exception ex) {
                    throw new AspireFixtureHookException(nameof(ConfigureAppHost), ex);
                }
            }).ConfigureAwait(false);

            builder.Services.AddSingleton(TestkitExecutionContext.LoggerFactory);

            try {
                ConfigureBuilder(builder);
            } catch (Exception ex) {
                throw new AspireFixtureHookException(nameof(ConfigureBuilder), ex);
            }

            _app = await builder.BuildAsync().ConfigureAwait(false);

            Logger.LogDebug("Aspire app built in {Elapsed}", stopwatch.Elapsed.Humanize());

            // Subscribe to resource logs BEFORE StartAsync so we're the first subscriber.
            // DCP only streams container stdout/stderr when subscribers exist, and the
            // testing framework's ResourceLoggerForwarderService (a hosted service) starts
            // during StartAsync. By subscribing first we capture the full log stream.
            _ = StartResourceLogSubscriptions();

            try {
                await BeforeStart().ConfigureAwait(false);
            } catch (Exception ex) {
                throw new AspireFixtureHookException(nameof(BeforeStart), ex);
            }

            // Parallel test assemblies each boot their own AppHost + DCP instance, and all
            // of them resolve the same persistent container name. When that container does
            // not exist yet, concurrent DCP instances race to create it and tear down each
            // other's half-created container; the loser fails startup with "No such
            // container" and the resource never reports healthy. Upstream bug:
            // https://github.com/dotnet/aspire/issues/10146
            // Serializing the launch window guarantees the first AppHost creates the
            // container and every later one takes the safe adoption path. Remove this
            // lock once the upstream issue is fixed.
            Logger.LogWarning(
                "Serializing AppHost launch across test processes to work around an Aspire " +
                "persistent-container race (https://github.com/dotnet/aspire/issues/10146)");

            // Scope the lock to the CI run: processes in one run share GITHUB_RUN_ID and serialize
            // against each other, but a fresh key per run can't inherit a foreign-owned lock file
            // left in the persistent runner's /tmp by an earlier run (a fixed path surfaced as
            // UnauthorizedAccessException). Off CI, a single fixed key serializes local test processes.
            var launchLockKey = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "local";

            using (await AppHostLaunchLock.AcquireAsync(launchLockKey, TimeSpan.FromMinutes(5)).ConfigureAwait(false)) {
                await App
                    .LaunchAsync(Options.StartupTimeout, Options.WaitBehavior, Options.ShouldWaitForResource)
                    .ConfigureAwait(false);
            }

            Logger.LogInformation("Aspire app started with all resources ready in {Elapsed}", stopwatch.Elapsed.Humanize());

            try {
                await AfterStart().ConfigureAwait(false);
            } catch (Exception ex) {
                throw new AspireFixtureHookException(nameof(AfterStart), ex);
            }
        } catch (Exception ex) {
            _initFailed = true;

            if (ex is AspireFixtureInitException)
                throw;

            throw new AspireFixtureInitException(
                ResourceLogs, ExpectedLogFilePath, Options.ResourceLogging.MaxExceptionLogLines, ex);
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync() {
        if (_app is null) return;

        try {
            WriteResourceLogFile();
        } catch (Exception ex) {
            if (_initFailed) {
                Logger.LogWarning(ex, "Failed to write resource log file (logs already included in startup exception)");
            } else {
                Logger.LogWarning(ex, "Failed to write resource log file, flushing to logger instead");
                FlushResourceLogs();
            }
        }

        try {
            await BeforeStop().ConfigureAwait(false);
        } catch (Exception ex) {
            throw new AspireFixtureHookException(nameof(BeforeStop), ex);
        }

        try {
            await App.StopAsync().ConfigureAwait(false);
            Logger.LogDebug("Aspire app stopped");
            await App.DisposeAsync().ConfigureAwait(false);
            Logger.LogDebug("Fixture disposed");
        }
        catch (Exception ex) {
            
            throw new AspireFixtureTeardownException(ExpectedLogFilePath, ex);
        }
    }
    
    #region ->> Resource Log Collection <<-

    readonly ConcurrentQueue<ResourceLogEntry> _resourceLogs = new();
    
    long _logSequence;
    long _lastFlushedSequence;
    string? _resolvedLogFilePath;

    string? ExpectedLogFilePath =>
        Options.ResourceLogging.Enabled
            ? Path.GetFullPath(Path.Combine(Options.ResourceLogging.LogDirectory, $"resource-logs-{TestSessionContext.Current?.Id ?? "unknown"}.jsonl"))
            : null;

    /// <summary>
    /// Watches <see cref="ResourceNotificationService"/> for new resources and subscribes
    /// to their log streams via <see cref="ResourceLoggerService"/>. This mirrors how
    /// Aspire's internal <c>ResourceLoggerForwarderService</c> works — it discovers
    /// resources by their runtime ID (which may differ from the model name) and subscribes
    /// to their log streams as they appear. All resource logs merge into a single
    /// chronological stream in <see cref="_resourceLogs"/>.
    /// </summary>
    async Task StartResourceLogSubscriptions() {
        try {
            var notificationService = App.Services.GetRequiredService<ResourceNotificationService>();
            var loggerService       = App.Services.GetRequiredService<ResourceLoggerService>();
            var subscribedIds       = new HashSet<string>();

            await foreach (var evt in notificationService.WatchAsync().ConfigureAwait(false)) {
                if (subscribedIds.Add(evt.ResourceId))
                    _ = BufferLogsAsync(loggerService, evt.ResourceId, evt.Resource.Name);
            }
        } catch (OperationCanceledException) { }
        
        
        async Task BufferLogsAsync(ResourceLoggerService loggerService, string resourceId, string resourceName) {
            try {
                // await loggerService.WatchAsync(resourceId)
                //     .SelectMany(batch => batch.Select(line => new ResourceLogEntry(resourceName, line)))
                //     .ForEachAsync(entry => _resourceLogs.Enqueue(entry))
                //     .ConfigureAwait(false);
                
                await foreach (var batch in loggerService.WatchAsync(resourceId).ConfigureAwait(false)) {
                    foreach (var line in batch)
                        _resourceLogs.Enqueue(new(Interlocked.Increment(ref _logSequence), resourceId, resourceName, line));
                }
            } catch (OperationCanceledException) { }
        }
    }
    
    /// <summary>
    /// Returns all buffered resource log entries in arrival order.
    /// </summary>
    public IReadOnlyList<ResourceLogEntry> ResourceLogs => _resourceLogs.ToArray();

    /// <summary>
    /// Writes all unflushed resource log entries to the fixture's <see cref="Logger"/>.
    /// Tracks the last flushed sequence so subsequent calls only emit new entries.
    /// </summary>
    public void FlushResourceLogs() {
        var entries = _resourceLogs
            .ToArray()
            .Where(e => e.Sequence > _lastFlushedSequence)
            .ToArray();

        if (entries.Length == 0) return;

        foreach (var entry in entries) {
            if (entry.Line.IsErrorMessage)
                Logger.LogError("[{Resource}] {Content}", entry.ResourceName, entry.Line.Content);
            else
                Logger.LogInformation("[{Resource}] {Content}", entry.ResourceName, entry.Line.Content);
        }

        _lastFlushedSequence = entries[^1].Sequence;
    }

    /// <summary>
    /// Writes the full chronological resource log stream to a file in the configured directory.
    /// The filename includes TUnit's <see cref="TestSessionContext.Id"/> for uniqueness per run.
    /// </summary>
    void WriteResourceLogFile() {
        if (!Options.ResourceLogging.Enabled) return;
        var dir = Options.ResourceLogging.LogDirectory;

        var entries = _resourceLogs.ToArray();
        if (entries.Length == 0) return;

        var sessionId = TestSessionContext.Current?.Id ?? "unknown";
        Directory.CreateDirectory(dir);

        _resolvedLogFilePath = Path.Combine(dir, $"resource-logs-{sessionId}.jsonl");

        using var fs   = new FileStream(_resolvedLogFilePath, FileMode.Create, FileAccess.Write);
        using var json = new Utf8JsonWriter(fs, new JsonWriterOptions { SkipValidation = true });
        foreach (var entry in entries) {
            JsonSerializer.Serialize(json, entry, ResourceLogJsonContext.Default.ResourceLogEntry);
            json.Flush();
            fs.Write("\n"u8);
            json.Reset();
        }

        Logger.LogInformation("Resource logs written to {Path} ({Count} entries)", _resolvedLogFilePath, entries.Length);
    }

    #endregion // Resource Log Collection
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

public sealed class AspireFixtureOptions {
    /// <summary>
    /// Maximum time allowed for the application to start and all resources to become ready.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How to wait for resources.
    /// </summary>
    public ResourceWaitBehavior WaitBehavior { get; set; } = ResourceWaitBehavior.AllHealthy;

    /// <summary>
    /// Predicate to determine which resources should be waited for.
    /// Defaults to all compute resources.
    /// </summary>
    public Func<IResource, bool> ShouldWaitForResource { get; set; } = r => r is IComputeResource and not IResourceWithParent;

    /// <summary>
    /// Resource log collection settings.
    /// </summary>
    public ResourceLoggingOptions ResourceLogging { get; set; } = new();
}

public sealed record ResourceLoggingOptions {
    /// <summary>
    /// Whether to write the full chronological resource log file on dispose.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Number of recent log entries to include in exception messages as a hint.
    /// These are the last N entries in arrival order across all resources.
    /// Default: 20.
    /// </summary>
    public int MaxExceptionLogLines { get; init; } = 20;

    /// <summary>
    /// Directory to write the full chronological resource log file on dispose.
    /// The file is named <c>resource-logs-{session-id}.jsonl</c> using TUnit's
    /// <see cref="TestSessionContext.Id"/> for uniqueness per test run.
    /// </summary>
    public string LogDirectory { get; init; } = ".artifacts/test-results";
}

/// <summary>
/// A single log line captured from a resource's stdout/stderr stream.
/// </summary>
public readonly record struct ResourceLogEntry(long Sequence, string ResourceId, string ResourceName, LogLine Line);

[JsonSerializable(typeof(ResourceLogEntry))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
partial class ResourceLogJsonContext : JsonSerializerContext;

public class AspireFixtureException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class AspireFixtureHookException(string hookName, Exception innerException)
    : AspireFixtureException($"{hookName} hook failed", innerException);

public class AspireFixtureInitException(
    IReadOnlyList<ResourceLogEntry> resourceLogs,
    string? logFilePath,
    int maxLogLines,
    Exception innerException
)
    : AspireFixtureException(
        GenerateLogSummary(
            DefaultMessage, resourceLogs, logFilePath,
            maxLogLines),
        innerException) {
    const string DefaultMessage = "Aspire fixture initialization failed";

    static string GenerateLogSummary(string message, IReadOnlyList<ResourceLogEntry> logs, string? logFilePath, int maxLines) {
        var sb = new StringBuilder(message);

        if (logs.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("--- recent resource logs ---");

            var tail = logs.Count > maxLines ? logs.Skip(logs.Count - maxLines) : logs;

            if (logs.Count > maxLines)
                sb.AppendLine($"  ... ({logs.Count - maxLines} earlier entries omitted)");

            foreach (var entry in tail) {
                var prefix = entry.Line.IsErrorMessage ? "ERR" : "INF";
                sb.AppendLine($"  [{prefix}] [{entry.ResourceName}] {entry.Line.Content}");
            }
        }

        if (logFilePath is not null) {
            var fullPath = Path.GetFullPath(logFilePath);
            sb.AppendLine();
            sb.AppendLine($"Full resource logs: {fullPath}");
        }

        return sb.ToString();
    }
}

public sealed class AspireFixtureTeardownException(string? logFilePath, Exception innerException) 
    : AspireFixtureException(FormatMessage(logFilePath), innerException) {
    const string DefaultMessage = "Aspire fixture teardown failed";

    static string FormatMessage(string? logFilePath) {
        if (logFilePath is null) return DefaultMessage;

        var fullPath = Path.GetFullPath(logFilePath);
        return $"{DefaultMessage}{Environment.NewLine}Full resource logs: {fullPath}";
    }
}