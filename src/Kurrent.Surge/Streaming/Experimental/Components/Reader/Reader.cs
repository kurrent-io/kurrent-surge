using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// Reads records from the log through a dedicated client owned by the reader.
/// </summary>
sealed class Reader(KurrentClientOptions options) : IReader {
    readonly KurrentClient _client = KurrentClient.Create(options);

    public IAsyncEnumerable<ReadMessage> Read(ReadRecordsOptions options, CancellationToken ct = default) =>
        _client.Streams.ReadRecords(options, ct);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
