// ReSharper disable CheckNamespace

namespace Kurrent.Diagnostics;

/// <summary>
/// OpenTelemetry semantic convention tag names for processor tracing.
/// </summary>
[PublicAPI]
public static class TracingTags {
    public static class Messaging {
        public const string System    = "messaging.system";
        public const string Operation = "messaging.operation";
        public const string MessageId = "messaging.message.id";
        public const string BodySize  = "messaging.message.body.size";
    }

    public static class Kurrent {
        public const string RecordStream    = "kurrent.record.stream";
        public const string RecordRevision  = "kurrent.record.revision";
        public const string RecordPosition  = "kurrent.record.position";
        public const string RecordPartition = "kurrent.record.partition";
        public const string RecordSchema    = "kurrent.record.schema";
        public const string RecordTimestamp = "kurrent.record.timestamp";
    }
}
