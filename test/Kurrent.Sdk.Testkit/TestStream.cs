using System.Collections;
using System.Collections.Immutable;
using Kurrent.Client.Streams;

namespace Testing.ArcadeRacer;

public record TestStream : IEnumerable<AppendRecord> {
    TestStream() { }

    public AppendStreamRequest AppendRequest { get; private init; }

    public StreamName Stream { get; private init; }

    public ImmutableList<AppendRecord> Records => AppendRequest.Records.ToImmutableList();

    public long LastTimestamp =>
        AppendRequest.Records.LastOrDefault()?.Timestamp.ToUnixTimeMilliseconds() ?? -1;

    public static TestStream From(AppendStreamRequest request) =>
        request.Checks.Length > 1
            ? throw new ArgumentException("Expected a single stream check", nameof(request))
            : new TestStream() {
                Stream        = request.Records[0].Stream,
                AppendRequest = request,
            };

    public IEnumerator<AppendRecord> GetEnumerator() => Records.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"{Stream} | recs: {Records.Count}";

    public static implicit operator AppendStreamRequest(TestStream testStream) => testStream.AppendRequest;
}
