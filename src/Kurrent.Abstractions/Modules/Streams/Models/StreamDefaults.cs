namespace Kurrent.Client.Streams;

// Canonical default for the filtered read/subscribe search window. It lives in the contract
// layer because the option types that default to it (SubscribeOptions, ReadRecordsOptions,
// ReadStreamOptions) moved here, while StreamsClientMapping — the original home — stays in the
// client. StreamsClientMapping.DefaultMaxSearchWindow now forwards to this single source.
static class StreamDefaults {
    internal const uint DefaultMaxSearchWindow = 4096;
}
