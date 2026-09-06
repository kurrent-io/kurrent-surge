using System.Runtime.CompilerServices;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// Consumes records from a subscription through a dedicated client owned by the consumer.
/// Non-record subscription messages (connection handshake, checkpoints) are filtered out.
/// </summary>
sealed class Consumer(KurrentClientOptions options) : IConsumer {
    readonly KurrentClient _client = KurrentClient.Create(options);

    public async IAsyncEnumerable<Record> Consume(ConsumerOptions options, [EnumeratorCancellation] CancellationToken ct = default) {
        await foreach (var message in _client.Streams.Subscribe(options, ct).ConfigureAwait(false))
            if (message.IsRecord)
                yield return message.AsRecord;
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
