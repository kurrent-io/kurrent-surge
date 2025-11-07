using Kurrent.Client.Streams;

namespace Kurrent.Client.PersistentSubscriptions;

class PersistentSubscriptionDroppedByServerException : KurrentException {
    public PersistentSubscriptionDroppedByServerException(string stream, string groupName, Exception? exception = null)
        : base($"Subscription group '{groupName}' on stream '{stream}' was dropped.", exception) {
        if (stream == StreamName.None)
            throw new ArgumentException("Stream name cannot be empty.", nameof(stream));

        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
    }
}
