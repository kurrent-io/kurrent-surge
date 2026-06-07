// ReSharper disable CheckNamespace

using System.Diagnostics;
using Kurrent.Client.Streams;

namespace Kurrent.Diagnostics;

[PublicAPI]
public static class RecordTracingExtensions {
    extension(Record source) {
        /// <summary>
        /// Creates standard OTel tags from record data.
        /// </summary>
        public ActivityTagsCollection ToTracingTags() => [
            new(TracingTags.Messaging.System, "kurrentdb"),
            new(TracingTags.Messaging.MessageId, source.Id.ToString()),
            new(TracingTags.Kurrent.RecordStream, source.Stream.ToString()),
            new(TracingTags.Kurrent.RecordRevision, source.Revision.Value),
            new(TracingTags.Kurrent.RecordPosition, source.Position.LogPosition.Value),
            new(TracingTags.Kurrent.RecordSchema, source.Schema.Schema.ToString()),
            new(TracingTags.Kurrent.RecordTimestamp, source.Timestamp)
        ];
    }
}
