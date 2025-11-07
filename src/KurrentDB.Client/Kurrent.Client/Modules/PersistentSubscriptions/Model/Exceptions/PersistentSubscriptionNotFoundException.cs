// using Kurrent.Client.Streams;
//
// namespace Kurrent.Client.PersistentSubscriptions;
//
// /// <summary>
// /// The exception that is thrown when a persistent subscription is not found.
// /// </summary>
// class PersistentSubscriptionNotFoundException : KurrentException {
//     /// <summary>
//     /// The group name.
//     /// </summary>
//     public readonly string GroupName;
//
//     /// <summary>
//     /// The stream name.
//     /// </summary>
//     public readonly StreamName Stream;
//
//     /// <summary>
//     /// Constructs a new <see cref="PersistentSubscriptionNotFoundException"/>.
//     /// </summary>
//     /// <exception cref="ArgumentNullException"></exception>
//     public PersistentSubscriptionNotFoundException(StreamName stream, string groupName, Exception? exception = null)
//         : base($"Subscription group '{groupName}' on stream '{stream}' does not exist.", exception) {
//         if (stream == StreamName.None)
//             throw new ArgumentException("Stream name cannot be empty.", nameof(stream));
//
//         ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
//
//         Stream = stream;
//         GroupName  = groupName;
//     }
// }
