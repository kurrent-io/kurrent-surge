// ReSharper disable CheckNamespace

using System.Diagnostics;
using Kurrent.Client;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Diagnostics;

/// <summary>
/// Constants for trace context propagation in record metadata.
/// </summary>
[PublicAPI]
public static class TraceContextKeys {
    public const string TraceId = "$traceId";
    public const string SpanId  = "$spanId";
}

/// <summary>
/// Extensions for extracting and injecting trace context from records and metadata.
/// </summary>
[PublicAPI]
public static class TracePropagationExtensions {
    extension(Metadata source) {
        /// <summary>
        /// Checks if trace context exists in metadata.
        /// </summary>
        public bool HasTraceContext() =>
            source.ContainsKey(TraceContextKeys.TraceId) &&
            source.ContainsKey(TraceContextKeys.SpanId);

        /// <summary>
        /// Extracts trace context from metadata.
        /// Returns null if trace IDs are missing or malformed.
        /// </summary>
        public ActivityContext? ExtractTraceContext() {
            if (!source.TryGetValue<string>(TraceContextKeys.TraceId, out var traceId) ||
                !source.TryGetValue<string>(TraceContextKeys.SpanId, out var spanId))
                return null;

            if (string.IsNullOrEmpty(traceId) || string.IsNullOrEmpty(spanId))
                return null;

            try {
                return new ActivityContext(
                    ActivityTraceId.CreateFromString(traceId.AsSpan()),
                    ActivitySpanId.CreateFromString(spanId.AsSpan()),
                    ActivityTraceFlags.Recorded,
                    isRemote: true
                );
            }
            catch {
                return null;
            }
        }

        /// <summary>
        /// Injects trace context from Activity.Current if not already present.
        /// </summary>
        public Metadata InjectTraceContext() =>
            Activity.Current is { } activity
                ? source.InjectTraceContext(activity)
                : source;

        /// <summary>
        /// Injects trace context from specific activity if not already present.
        /// </summary>
        public Metadata InjectTraceContext(Activity activity) =>
            source.InjectTraceContext(activity.Context);

        /// <summary>
        /// Injects trace context from specific context if not already present.
        /// </summary>
        public Metadata InjectTraceContext(ActivityContext context) =>
            source
                .WithIf(TraceContextKeys.TraceId, context.TraceId.ToString(), !source.ContainsKey(TraceContextKeys.TraceId))
                .WithIf(TraceContextKeys.SpanId, context.SpanId.ToString(), !source.ContainsKey(TraceContextKeys.SpanId));
    }

    extension(Record source) {
        /// <summary>
        /// Checks if record has trace context in metadata.
        /// </summary>
        public bool HasTraceContext() => source.Metadata.HasTraceContext();

        /// <summary>
        /// Extracts trace context from record metadata.
        /// </summary>
        public ActivityContext? ExtractTraceContext() => source.Metadata.ExtractTraceContext();
    }

    extension(RecordBatch source) {
        /// <summary>
        /// Extracts trace links from all records with valid trace context.
        /// Returns null if no records have trace context (avoids allocation).
        /// </summary>
        public List<ActivityLink>? ExtractTraceLinks() {
            List<ActivityLink>? links = null;

            foreach (var record in source) {
                if (record.ExtractTraceContext() is not { } ctx)
                    continue;

                links ??= [];
                links.Add(new ActivityLink(ctx, new ActivityTagsCollection {
                    { TracingTags.Messaging.MessageId, record.Id.ToString() },
                    { TracingTags.Kurrent.RecordStream, record.Stream.ToString() }
                }));
            }

            return links;
        }
    }
}
