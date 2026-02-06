// ReSharper disable CheckNamespace

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Kurrent.Diagnostics;

[PublicAPI]
public static class ActivitySourceExtensions {
    extension(ActivitySource activitySource) {
        /// <summary>
        /// Creates a trace operation with the specified configuration.
        /// </summary>
        public TraceOperation Trace(string activityName, Action<TraceOptions>? configure = null) {
            if (configure is null)
                return new TraceOperation(activitySource.CreateActivity(activityName, ActivityKind.Internal));

            var options = new TraceOptions();

            configure(options);

            var activity = activitySource.CreateActivity(
                activityName,
                options.ActivityKind,
                options.ParentContext,
                options.Tags,
                options.Links
            );

            return new TraceOperation(activity);
        }

        /// <summary>
        /// Begins a manually managed trace scope with the specified configuration.
        /// Call <see cref="TraceScope.Complete"/> or <see cref="TraceScope.Fail"/> when done.
        /// Disposing without completion marks the trace as error (safety net).
        /// </summary>
        public TraceScope BeginTrace(string activityName, Action<TraceOptions>? configure = null) {
            if (configure is null) {
                var activity = new TracedActivity(activitySource.CreateActivity(activityName, ActivityKind.Internal));
                return new TraceScope(activity.Start());
            }

            var options = new TraceOptions();

            configure(options);

            var configured = new TracedActivity(
                activitySource.CreateActivity(
                    activityName,
                    options.ActivityKind,
                    options.ParentContext,
                    options.Tags,
                    options.Links
                ));

            return new TraceScope(configured.Start());
        }
    }
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

    /// <summary>
    /// Adds multiple tags from a collection.
    /// </summary>
    public TraceOptions WithTags(ActivityTagsCollection tags) {
        foreach (var tag in tags)
            Tags[tag.Key] = tag.Value;
        return this;
    }

    /// <summary>
    /// Adds multiple tags from key-value pairs.
    /// </summary>
    public TraceOptions WithTags(IEnumerable<KeyValuePair<string, object?>> tags) {
        foreach (var tag in tags)
            Tags[tag.Key] = tag.Value;
        return this;
    }

    /// <summary>
    /// Adds multiple links from a collection. Accepts null for convenience.
    /// </summary>
    public TraceOptions WithLinks(IEnumerable<ActivityLink>? links) {
        if (links is not null)
            Links.AddRange(links);
        return this;
    }
}

/// <summary>
/// A configured trace operation ready for execution.
/// </summary>
public readonly struct TraceOperation {
    readonly TracedActivity _activity;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TraceOperation(Activity? activity) =>
        _activity = new TracedActivity(activity);

    /// <summary>
    /// Executes an async operation within the trace context with state (avoids closure allocation).
    /// </summary>
    public async ValueTask RunAsync<TState>(TState state, Func<TraceContext, TState, ValueTask> action) {
        using var activity = _activity.Start();

        var ctx = new TraceContext(activity);

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

        var ctx = new TraceContext(activity);

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
/// A manually managed trace scope following the 2PC pattern.
/// Acquire the scope, perform work, then <see cref="Complete"/> or <see cref="Fail"/>.
/// Disposing without completion marks the trace as error (safety net).
/// </summary>
public struct TraceScope : IDisposable {
    readonly TracedActivity _activity;
    bool _completed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TraceScope(TracedActivity activity) {
        _activity = activity;
        _completed = false;
    }

    /// <summary>
    /// Gets whether this scope is actually recording (has a listener).
    /// </summary>
    public bool IsRecording => _activity.IsRecording;

    /// <summary>
    /// Gets the start time. Returns current time if not recording.
    /// </summary>
    public DateTimeOffset StartTimeUtc => _activity.StartTimeUtc;

    /// <summary>
    /// Sets a tag on the activity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceScope SetTag(string key, object? value) {
        _activity.SetTag(key, value);
        return this;
    }

    /// <summary>
    /// Adds a named event to the activity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceScope AddEvent(string name) {
        _activity.AddEvent(name);
        return this;
    }

    /// <summary>
    /// Adds a named event with a single tag to the activity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceScope AddEvent(string name, string tagKey, object? tagValue) {
        _activity.AddEvent(name, tagKey, tagValue);
        return this;
    }

    /// <summary>
    /// Adds a named event with multiple tags to the activity.
    /// </summary>
    public TraceScope AddEvent(string name, params (string Key, object? Value)[] tags) {
        _activity.AddEvent(name, tags);
        return this;
    }

    /// <summary>
    /// Marks the scope as successfully completed.
    /// </summary>
    public void Complete() {
        if (_completed) return;
        _completed = true;
        _activity.SetStatus(ActivityStatusCode.Ok);
        _activity.Dispose();
    }

    /// <summary>
    /// Marks the scope as failed with the given exception.
    /// </summary>
    public void Fail(Exception ex) {
        if (_completed) return;
        _completed = true;
        _activity.RecordException(ex);
        _activity.Dispose();
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_completed) return;
        _completed = true;
        _activity.SetStatus(ActivityStatusCode.Error, "Scope disposed without completion");
        _activity.Dispose();
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
        public ValueTask RunAsync(Func<TraceContext, ValueTask> action) =>
            op.RunAsync(action, static (ctx, a) => a(ctx));

        /// <summary>
        /// Executes an async operation within the trace context and returns a result.
        /// </summary>
        public ValueTask<TResult> RunAsync<TResult>(Func<TraceContext, ValueTask<TResult>> action) =>
            op.RunAsync(action, static (ctx, a) => a(ctx));

        /// <summary>
        /// Executes a synchronous operation within the trace context.
        /// </summary>
        public void Run(Action<TraceContext> action) =>
            op.RunAsync(action, static (ctx, a) => {
                    a(ctx);
                    return ValueTask.CompletedTask;
                }).AsTask().GetAwaiter().GetResult();

        /// <summary>
        /// Executes a synchronous operation within the trace context with state.
        /// </summary>
        public void Run<TState>(TState state, Action<TraceContext, TState> action) =>
            op.RunAsync(state, (ctx, s) => {
                action(ctx, s);
                return ValueTask.CompletedTask;
            }).AsTask().GetAwaiter().GetResult();

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
/// Extension methods for <see cref="Activity"/> providing simplified event recording.
/// </summary>
[PublicAPI]
public static class ActivityExtensions {
    extension(Activity activity) {
        /// <summary>
        /// Adds a named event to the activity.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Activity AddEvent(string name) {
            activity.AddEvent(new ActivityEvent(name));
            return activity;
        }

        /// <summary>
        /// Adds a named event with a single tag to the activity.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Activity AddEvent(string name, string tagKey, object? tagValue) {
            activity.AddEvent(new ActivityEvent(name, tags: new ActivityTagsCollection { { tagKey, tagValue } }));
            return activity;
        }

        /// <summary>
        /// Adds a named event with multiple tags to the activity.
        /// </summary>
        public Activity AddEvent(string name, params (string Key, object? Value)[] tags) {
            var collection = new ActivityTagsCollection();
            foreach (var (key, value) in tags)
                collection[key] = value;
            activity.AddEvent(new ActivityEvent(name, tags: collection));
            return activity;
        }

        /// <summary>
        /// Records an exception following OpenTelemetry semantic conventions.
        /// </summary>
        public void RecordException(Exception ex) {
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.AddEvent(
                new ActivityEvent(
                    "exception", tags: new ActivityTagsCollection {
                        { "exception.type", ex.GetType().FullName },
                        { "exception.message", ex.Message },
                        { "exception.stacktrace", ex.StackTrace }
                    }
                )
            );
        }
    }
}

/// <summary>
/// Null-object wrapper around Activity that eliminates null checks.
/// When no listener is registered, all operations are safe no-ops.
/// </summary>
public readonly struct TracedActivity : IDisposable {
    readonly Activity? _inner;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TracedActivity(Activity? activity) => _inner = activity;

    /// <summary>
    /// Gets whether this activity is actually recording (has a listener).
    /// </summary>
    public bool IsRecording =>
        _inner is not null;

    /// <summary>
    /// Gets the start time. Returns current time if not recording.
    /// </summary>
    public DateTimeOffset StartTimeUtc =>
        _inner?.StartTimeUtc ?? DateTimeOffset.UtcNow;

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
    /// Adds a named event to the activity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TracedActivity AddEvent(string name) {
        _inner?.AddEvent(name);
        return this;
    }

    /// <summary>
    /// Adds a named event with a single tag to the activity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TracedActivity AddEvent(string name, string tagKey, object? tagValue) {
        _inner?.AddEvent(name, tagKey, tagValue);
        return this;
    }

    /// <summary>
    /// Adds a named event with multiple tags to the activity.
    /// </summary>
    public TracedActivity AddEvent(string name, params (string Key, object? Value)[] tags) {
        _inner?.AddEvent(name, tags);
        return this;
    }

    /// <summary>
    /// Adds a raw event to the activity.
    /// </summary>
    public TracedActivity AddEvent(ActivityEvent evt) {
        _inner?.AddEvent(evt);
        return this;
    }

    /// <summary>
    /// Records an exception following OpenTelemetry semantic conventions.
    /// </summary>
    public void RecordException(Exception ex) {
        _inner?.RecordException(ex);
    }

    /// <inheritdoc />
    public void Dispose() => _inner?.Dispose();
}

/// <summary>
/// Context passed to traced operations providing tagging and event capabilities.
/// </summary>
public readonly struct TraceContext {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TraceContext(TracedActivity activity) =>
        Activity = activity;

    /// <summary>
    /// Gets the current activity.
    /// </summary>
    public TracedActivity Activity { get; }
}
