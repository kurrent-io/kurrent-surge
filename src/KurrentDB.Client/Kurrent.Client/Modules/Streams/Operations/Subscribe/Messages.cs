using System.Threading.Channels;
using Kurrent.Variant;
using static System.Threading.Interlocked;

namespace Kurrent.Client.Streams;

[PublicAPI]
public record AllSubscriptionOptions : ReadAllOptions {
    new ReadDirection Direction { get; init; }
    new long          Limit     { get; init; }
}

[PublicAPI]
public record StreamSubscriptionOptions : ReadStreamOptions {
    new ReadDirection Direction { get; init; }
    new long          Limit     { get; init; }
}

[PublicAPI]
public record Subscription : Messages {
    internal Subscription(string subscriptionId, Func<Channel<ReadMessage>> channelFactory) : base(channelFactory) => SubscriptionId = subscriptionId;

    /// <summary>
    /// Gets the unique identifier associated with the subscription.
    /// </summary>
    public string SubscriptionId { get; }
}

//
// [PublicAPI]
// public sealed record Subscription : IAsyncEnumerable<ReadMessage>, IAsyncDisposable {
//     internal Subscription(string subscriptionId, Func<Channel<ReadMessage>> channelFactory) {
//         SubscriptionId = subscriptionId;
//         _lazyChannel   = new Lazy<Channel<ReadMessage>>(channelFactory);
//     }
//
//     Lazy<Channel<ReadMessage>> _lazyChannel;
//
//     int _disposed;
//     int _enumeratorCreated;
//
//     Channel<ReadMessage> Channel => _lazyChannel.Value;
//
//     /// <summary>
//     /// Gets the unique identifier associated with the subscription.
//     /// </summary>
//     public string SubscriptionId { get; }
//
//     /// <summary>
//     /// The stream of subscription messages, which can include records or heartbeat notifications,
//     /// depending on the subscription type and the state of the subscribed stream.
//     /// </summary>
//     public IAsyncEnumerable<ReadMessage> Messages => this;
//
//     /// <summary>
//     /// The number of messages currently queued for processing
//     /// </summary>
//     public int QueuedMessages => _lazyChannel.IsValueCreated ? Channel.Reader.Count : 0;
//
//     /// <summary>
//     /// Flushes the buffer and waits until all messages are processed
//     /// </summary>
//     /// <param name="timeout">An optional timeout duration to wait. Defaults to 30 seconds if not specified.</param>
//     public async ValueTask<bool> Flush(TimeSpan? timeout = null) {
//         if (Interlocked.CompareExchange(ref _disposed, 0, 0) == 1)
//             return true;
//
//         if (!_lazyChannel.IsValueCreated)
//             return true;
//
//         Channel.Writer.TryComplete();
//
//         var completedTask = await Task
//             .WhenAny(Channel.Reader.Completion, Task.Delay(timeout ?? TimeSpan.FromSeconds(30)))
//             .ConfigureAwait(false);
//
//         return completedTask == Channel.Reader.Completion;
//     }
//
//     public async IAsyncEnumerator<ReadMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default) {
//         if (Interlocked.CompareExchange(ref _disposed, 0, 0) == 1)
//             throw new ObjectDisposedException(nameof(Subscription));
//
//         if (Interlocked.CompareExchange(ref _enumeratorCreated, 1, 0) == 1)
//             throw new InvalidOperationException("Only one enumerator can be active at a time");
//
//         // return Channel.Reader.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator(CancellationToken.None);
//
//         var reader = Channel.Reader;
//
//         while (true) {
//             bool dataAvailable;
//             try {
//                 dataAvailable = await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
//             }
//             catch (OperationCanceledException) {
//                 yield break;
//             }
//             catch (ObjectDisposedException) {
//                 yield break;
//             }
//
//             if (!dataAvailable)
//                 break;
//
//             while (reader.TryRead(out var item))
//                 yield return item;
//         }
//     }
//
//     public ValueTask DisposeAsync() {
//         if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
//             return ValueTask.CompletedTask;
//
//         if (_lazyChannel.IsValueCreated)
//             Channel.Writer.TryComplete();
//
//         return ValueTask.CompletedTask;
//     }
// }
