// ReSharper disable CheckNamespace

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Kurrent.Client.Processors.Diagnostics;

/// <summary>
/// Factory for creating diagnostic traces with automatic timing and activity management.
/// </summary>
public sealed class SystemDiagnostics {
    readonly ActivitySource   _activitySource;
    readonly DiagnosticSource _diagnosticSource;

    SystemDiagnostics(string sourceName, string version) {
        _activitySource   = new ActivitySource(sourceName, version);
        _diagnosticSource = new DiagnosticListener(sourceName);
    }

    public static SystemDiagnostics Create(string sourceName, string version = "1.0.0") =>
        new(sourceName, version);

    /// <summary>
    /// Creates a trace operation with the specified configuration.
    /// </summary>
    public TraceOperation Trace(string activityName, Action<TraceOptions>? configure = null) {
        if (configure is null)
            return new TraceOperation(this, _activitySource.CreateActivity(activityName, ActivityKind.Internal));

        var options = new TraceOptions();

        configure(options);

        var activity = _activitySource.CreateActivity(
            activityName,
            options.ActivityKind,
            options.ParentContext,
            options.Tags,
            options.Links
        );

        return new TraceOperation(this, activity);
    }

    /// <summary>
    /// Publishes an event to the diagnostic source using the type name as the event name.
    /// </summary>
    public void Publish<T>(T message) where T : notnull {
        var messageName = typeof(T).Name;
        if (_diagnosticSource.IsEnabled(messageName))
            _diagnosticSource.Write(messageName, message);
    }

    public bool IsEnabled(string eventName) =>
        _diagnosticSource.IsEnabled(eventName);
}

/// <summary>
/// Configuration for a trace operation. Must be set before the activity starts.
/// </summary>
public sealed class TraceOptions {
    internal ActivityKind           ActivityKind  { get; private set; } = ActivityKind.Internal;
    internal ActivityContext        ParentContext { get; private set; }
    internal ActivityTagsCollection Tags          { get; } = [];
    internal List<ActivityLink>     Links         { get; } = [];

    /// <summary>
    /// Sets the activity kind.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceOptions Kind(ActivityKind kind) {
        ActivityKind = kind;
        return this;
    }

    /// <summary>
    /// Sets the parent context for distributed tracing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceOptions Parent(ActivityContext parent) {
        ParentContext = parent;
        return this;
    }

    /// <summary>
    /// Sets the parent from an existing activity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceOptions Parent(Activity parent) => Parent(parent.Context);

    /// <summary>
    /// Adds a tag to the activity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceOptions Tag(string key, object? value) {
        Tags[key] = value;
        return this;
    }

    /// <summary>
    /// Adds a link to another trace context.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceOptions Link(ActivityContext context, ActivityTagsCollection? tags = null) {
        Links.Add(new ActivityLink(context, tags));
        return this;
    }

    /// <summary>
    /// Adds a link to another activity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceOptions Link(Activity activity, ActivityTagsCollection? tags = null) => Link(activity.Context, tags);
}

/// <summary>
/// A configured trace operation ready for execution.
/// </summary>
public readonly struct TraceOperation {
    readonly SystemDiagnostics _tracer;
    readonly TracedActivity    _activity;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TraceOperation(SystemDiagnostics tracer, Activity? activity) {
        _tracer   = tracer;
        _activity = new TracedActivity(activity);
    }

    /// <summary>
    /// Executes an async operation within the trace context with state (avoids closure allocation).
    /// </summary>
    public async ValueTask RunAsync<TState>(TState state, Func<TraceContext, TState, ValueTask> action) {
        using var activity = _activity.Start();

        var ctx = new TraceContext(_tracer, activity);

        try {
            await action(ctx, state).ConfigureAwait(false);
            activity.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex) {
            activity.RecordException(ex);
            throw;
        }
    }

    /// <summary>
    /// Executes an async operation within the trace context with state and returns a result.
    /// </summary>
    public async ValueTask<TResult> RunAsync<TState, TResult>(TState state, Func<TraceContext, TState, ValueTask<TResult>> action) {
        using var activity = _activity.Start();

        var ctx = new TraceContext(_tracer, activity);

        try {
            var result = await action(ctx, state).ConfigureAwait(false);
            activity.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex) {
            activity.RecordException(ex);
            throw;
        }
    }
}

/// <summary>
/// Extension methods for TraceOperation convenience overloads.
/// </summary>
public static class TraceOperationExtensions {
    extension(TraceOperation op) {
        /// <summary>
        /// Executes an async operation within the trace context.
        /// </summary>
        public ValueTask RunAsync(Func<TraceContext, ValueTask> action) => op.RunAsync(action, static (ctx, a) => a(ctx));

        /// <summary>
        /// Executes an async operation within the trace context and returns a result.
        /// </summary>
        public ValueTask<TResult> RunAsync<TResult>(Func<TraceContext, ValueTask<TResult>> action) => op.RunAsync(action, static (ctx, a) => a(ctx));

        /// <summary>
        /// Executes a synchronous operation within the trace context.
        /// </summary>
        public void Run(Action<TraceContext> action) =>
            op.RunAsync(
                    action, static (ctx, a) => {
                        a(ctx);
                        return ValueTask.CompletedTask;
                    }
                )
                .AsTask().GetAwaiter().GetResult();

        /// <summary>
        /// Executes a synchronous operation within the trace context with state.
        /// </summary>
        public void Run<TState>(TState state, Action<TraceContext, TState> action) =>
            op.RunAsync(
                    state, (ctx, s) => {
                        action(ctx, s);
                        return ValueTask.CompletedTask;
                    }
                )
                .AsTask().GetAwaiter().GetResult();

        /// <summary>
        /// Executes a synchronous operation within the trace context and returns a result.
        /// </summary>
        public TResult Run<TResult>(Func<TraceContext, TResult> action) =>
            op.RunAsync(action, static (ctx, a) => ValueTask.FromResult(a(ctx)))
                .AsTask().GetAwaiter().GetResult();

        /// <summary>
        /// Executes a synchronous operation within the trace context with state and returns a result.
        /// </summary>
        public TResult Run<TState, TResult>(TState state, Func<TraceContext, TState, TResult> action) =>
            op.RunAsync(state, (ctx, s) => ValueTask.FromResult(action(ctx, s)))
                .AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Null-object wrapper around Activity that eliminates null checks.
/// When no listener is registered, all operations are safe no-ops.
/// </summary>
public readonly struct TracedActivity : IDisposable {
    readonly Activity? _inner;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TracedActivity(Activity? activity) =>
        _inner = activity;

    /// <summary>
    /// Gets whether this activity is actually recording (has a listener).
    /// </summary>
    public bool IsRecording => _inner is not null;

    /// <summary>
    /// Gets the start time. Returns current time if not recording.
    /// </summary>
    public DateTimeOffset StartTimeUtc {
        get => _inner?.StartTimeUtc ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Starts the activity.
    /// </summary>
    public TracedActivity Start() {
        _inner?.Start();
        return this;
    }

    /// <summary>
    /// Sets a tag on the activity.
    /// </summary>
    public TracedActivity SetTag(string key, object? value) {
        _inner?.SetTag(key, value);
        return this;
    }

    /// <summary>
    /// Sets the status of the activity.
    /// </summary>
    public TracedActivity SetStatus(ActivityStatusCode code, string? description = null) {
        _inner?.SetStatus(code, description);
        return this;
    }

    /// <summary>
    /// Adds an event to the activity.
    /// </summary>
    public TracedActivity AddEvent(ActivityEvent evt) {
        _inner?.AddEvent(evt);
        return this;
    }

    /// <summary>
    /// Records an exception following OpenTelemetry semantic conventions.
    /// </summary>
    public void RecordException(Exception ex) {
        if (_inner is null) return;

        _inner.SetStatus(ActivityStatusCode.Error, ex.Message);
        _inner.AddEvent(
            new ActivityEvent(
                "exception", tags: new ActivityTagsCollection {
                    { "exception.type", ex.GetType().FullName },
                    { "exception.message", ex.Message },
                    { "exception.stacktrace", ex.StackTrace }
                }
            )
        );
    }

    /// <inheritdoc />
    public void Dispose() => _inner?.Dispose();
}

/// <summary>
/// Context passed to traced operations providing tagging and event capabilities.
/// </summary>
public readonly struct TraceContext {
    readonly SystemDiagnostics _tracer;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TraceContext(SystemDiagnostics tracer, TracedActivity activity) {
        _tracer   = tracer;
        Activity = activity;
    }

    /// <summary>
    /// Gets the current activity.
    /// </summary>
    public TracedActivity Activity { get; }

    /// <summary>
    /// Publishes an event to the diagnostic source.
    /// </summary>
    public void Publish<T>(T message) where T : notnull => _tracer.Publish(message);
}
