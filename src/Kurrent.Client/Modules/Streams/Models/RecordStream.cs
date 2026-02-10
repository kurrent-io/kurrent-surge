using System.Threading.Channels;
using Kurrent.Variant;
using static System.Threading.Interlocked;

namespace Kurrent.Client.Streams;

[PublicAPI]
public record RecordStream : RecordStream<ReadRecordsResult> {
    protected internal RecordStream(Func<Channel<ReadRecordsResult>> channelFactory) : base(channelFactory) { }
}

[PublicAPI]
public record RecordStream<T> : IAsyncEnumerable<T>, IAsyncDisposable {
    int _disposed;
    int _enumeratorCreated;

    readonly Lazy<Channel<T>> _lazyChannel;

    protected internal RecordStream(Func<Channel<T>> channelFactory) =>
        _lazyChannel = new Lazy<Channel<T>>(channelFactory);

    Channel<T> Channel => _lazyChannel.Value;

    /// <summary>
    /// The number of items currently queued for processing
    /// </summary>
    public int QueuedMessages => _lazyChannel.IsValueCreated ? Channel.Reader.Count : 0;

    public async ValueTask DisposeAsync() {
        if (CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        if (!_lazyChannel.IsValueCreated)
            return;

        Channel.Writer.TryComplete();

        // wait for completion but don't propagate exceptions
        // errors should surface through enumeration, not dispose
        try {
            await Channel.Reader.Completion.ConfigureAwait(false);
        } catch {
            // swallow - exception was already propagated via GetAsyncEnumerator
        }
    }

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(CompareExchange(ref _disposed, 0, 0) == 1, this);

        if (CompareExchange(ref _enumeratorCreated, 1, 0) == 1)
            throw new InvalidOperationException("Items already enumerated.");

        if (ct.IsCancellationRequested)
            yield break;

        var reader = Channel.Reader;

        while (true) {
            bool canRead;

            try {
                canRead = await reader.WaitToReadAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                yield break;
            }

            if (!canRead) {
                // propagate any exception from channel completion
                try {
                    await reader.Completion.ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    // ignore cancellation
                }

                yield break;
            }

            while (reader.TryRead(out var message))
                yield return message;
        }
    }
}

/// <summary>
/// The result of a read records operation, which can be either a <see cref="Record"/> or a <see cref="Heartbeat"/>.
/// </summary>
public readonly partial record struct ReadRecordsResult : IVariant<Record, Heartbeat> {
    internal static readonly ReadRecordsResult Irrelevant = new();
}
