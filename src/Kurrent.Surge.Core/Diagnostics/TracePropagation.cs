// ReSharper disable CheckNamespace

using System.Diagnostics;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Diagnostics;

/// <summary>
/// Legacy platform keys for trace context in record metadata. Superseded by W3C Trace Context
/// propagation (<see cref="TracePropagationExtensions"/>); retained because the metadata JSON
/// converter still special-cases these literals.
/// </summary>
[PublicAPI]
public static class TraceContextKeys {
    public const string TraceId = "$traceId";
    public const string SpanId  = "$spanId";
}

/// <summary>
/// Extensions for propagating trace context through record metadata as W3C Trace Context.
/// </summary>
/// <remarks>
/// The propagator is pinned to <see cref="DistributedContextPropagator.CreateW3CPropagator"/>
/// rather than <see cref="DistributedContextPropagator.Current"/>: record metadata is a durable,
/// cross-SDK platform contract — its wire shape must not vary with whatever propagator the host
/// process configures for its own transports.
/// </remarks>
[PublicAPI]
public static class TracePropagationExtensions {
    static readonly DistributedContextPropagator Propagator = DistributedContextPropagator.CreateW3CPropagator();

    static void Setter(object? carrier, string fieldName, string fieldValue) =>
        ((Metadata)carrier!)[fieldName] = fieldValue;

    static void Getter(object? carrier, string fieldName, out string? fieldValue, out IEnumerable<string>? fieldValues) {
        fieldValues = null;
        fieldValue  = ((Metadata)carrier!).TryGetValue<string>(fieldName, out var value) ? value : null;
    }

    extension(Metadata source) {
        /// <summary>
        /// Checks if propagated trace context is present and well-formed.
        /// </summary>
        public bool HasTraceContext() => source.ExtractTraceContext() is not null;

        /// <summary>
        /// Extracts W3C trace context. Returns <see langword="null"/> when absent or malformed.
        /// </summary>
        public ActivityContext? ExtractTraceContext() {
            Propagator.ExtractTraceIdAndState(source, Getter, out var traceParent, out var traceState);

            return traceParent is not null && ActivityContext.TryParse(traceParent, traceState, isRemote: true, out var context)
                ? context
                : null;
        }

        /// <summary>
        /// Injects the current activity's trace context. No-op when no activity is current.
        /// </summary>
        public Metadata InjectTraceContext() =>
            Activity.Current is { } activity ? source.InjectTraceContext(activity) : source;

        /// <summary>
        /// Injects the activity's trace context, replacing any existing context.
        /// </summary>
        /// <remarks>
        /// Replacement is deliberate: each processing hop advances the propagated context;
        /// ancestry is preserved by the span hierarchy, not by the carrier.
        /// </remarks>
        public Metadata InjectTraceContext(Activity activity) {
            Propagator.Inject(activity, source, Setter);
            return source;
        }
    }

    extension(Record source) {
        /// <summary>
        /// Checks if the record's metadata carries propagated trace context.
        /// </summary>
        public bool HasTraceContext() => source.Metadata.HasTraceContext();

        /// <summary>
        /// Extracts W3C trace context from the record's metadata.
        /// </summary>
        public ActivityContext? ExtractTraceContext() => source.Metadata.ExtractTraceContext();

        /// <summary>
        /// Injects the activity's trace context into the record's metadata.
        /// </summary>
        public Record InjectTraceContext(Activity activity) {
            source.Metadata.InjectTraceContext(activity);
            return source;
        }
    }

    extension(IReadOnlyList<Record> source) {
        /// <summary>
        /// Extracts trace links from all records with valid trace context.
        /// Returns <see langword="null"/> if no records have trace context (avoids allocation).
        /// </summary>
        public List<ActivityLink>? ExtractTraceLinks() {
            List<ActivityLink>? links = null;

            foreach (var record in source) {
                if (record.ExtractTraceContext() is not { } context)
                    continue;

                links ??= [];
                links.Add(new ActivityLink(context, new ActivityTagsCollection {
                    { TracingTags.Messaging.MessageId, record.Id.ToString() },
                    { TracingTags.Kurrent.RecordStream, record.Stream.ToString() }
                }));
            }

            return links;
        }
    }
}
