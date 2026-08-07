using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Experimental;

interface IReader : IAsyncDisposable {
    IAsyncEnumerable<ReadMessage> Read(ReadRecordsOptions options, CancellationToken ct = default);

    //
    // IAsyncEnumerable<Record> Read(LogPosition position, ReadDirection direction, RecordFilter filter, int maxCount, CancellationToken cancellationToken = default);
    //
    // IAsyncEnumerable<Record> Read(Stream streamId, Revision revision, ReadDirection direction, int maxCount, CancellationToken cancellationToken = default);
}