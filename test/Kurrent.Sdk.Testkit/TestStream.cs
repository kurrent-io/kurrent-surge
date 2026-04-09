using System.Collections;
using System.Collections.Immutable;
using Kurrent.Client.Streams;

namespace Testing.ArcadeRacer;

public record TestStream : IEnumerable<AppendRecord> {
    TestStream() { }

    public AppendStreamRequest AppendRequest { get; private init; }

    public StreamName Stream { get; private init; }

    public ImmutableArray<AppendRecord> Records => AppendRequest.Records;

    public long LastTimestamp =>
        AppendRequest.Records.LastOrDefault()?.Timestamp.ToUnixTimeMilliseconds() ?? -1;

    public static TestStream From(AppendStreamRequest request) =>
        new() {
            Stream        = request.Records[0].Stream,
            AppendRequest = request,
        };

    public IEnumerator<AppendRecord> GetEnumerator() => ((IList<AppendRecord>)Records).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"{Stream} | recs: {Records.Length}";

    public static implicit operator AppendStreamRequest(TestStream testStream) => testStream.AppendRequest;
}
