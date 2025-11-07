using Kurrent.Client.Streams;

namespace Kurrent.Client.PersistentSubscriptions;

class MaximumSubscribersReachedException : KurrentException {
    public MaximumSubscribersReachedException(StreamName stream, string groupName, Exception? exception = null)
        : base($"Maximum subscriptions reached for subscription group '{groupName}' on stream '{stream}.'", exception) {
        if (stream == StreamName.None)
            throw new ArgumentException("Stream name cannot be empty.", nameof(stream));

        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
    }
}
