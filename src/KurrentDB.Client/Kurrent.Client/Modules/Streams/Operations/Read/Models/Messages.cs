using System.Threading.Channels;
using Kurrent.Variant;
using static System.Threading.Interlocked;

namespace Kurrent.Client.Streams;

public readonly partial record struct ReadMessage : IVariant<Record, Heartbeat> {
    public static readonly ReadMessage None;
}

[PublicAPI]
public sealed record Messages : IAsyncEnumerable<ReadMessage>, IAsyncDisposable {
    int _disposed;
    int _enumeratorCreated;

    Lazy<Channel<ReadMessage>> _lazyChannel;

    internal Messages(Func<Channel<ReadMessage>> channelFactory) =>
        _lazyChannel = new Lazy<Channel<ReadMessage>>(channelFactory);

    Channel<ReadMessage> Channel => _lazyChannel.Value;

    /// <summary>
    /// The number of messages currently queued for processing
    /// </summary>
    public int QueuedMessages => _lazyChannel.IsValueCreated ? Channel.Reader.Count : 0;

    public async ValueTask DisposeAsync() {
        if (!_lazyChannel.IsValueCreated || CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        Channel.Writer.TryComplete();

        if (Channel.Reader.Completion.IsFaulted)
            await Channel.Reader.Completion;
    }

    public async IAsyncEnumerator<ReadMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(CompareExchange(ref _disposed, 0, 0) == 1, nameof(Subscription));

        if (CompareExchange(ref _enumeratorCreated, 1, 0) == 1)
            throw new InvalidOperationException("Only one enumerator can be active at a time");

        var reader = Channel.Reader;

        while (true) {
            ReadMessage message;
            try {
                message = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                yield break;
            }
            // catch (ObjectDisposedException) {
            //     yield break;
            // }
            catch (ChannelClosedException) {
                yield break;
            }

            if (message == ReadMessage.None)
                break;

            yield return message;
        }
    }
}
