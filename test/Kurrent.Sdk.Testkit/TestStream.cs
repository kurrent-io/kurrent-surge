using System.Collections;
using System.Collections.Immutable;
using Kurrent.Client.Streams;

namespace Testing.ArcadeRacer;

public record TestStream : IEnumerable<AppendRecord> {
    TestStream() { }

    public AppendToStreamRequest AppendRequest { get; private init; }

    public StreamName Stream { get; private init; }

    public ImmutableList<AppendRecord> Records => AppendRequest.Records.ToImmutableList();

    public long LastTimestamp =>
        AppendRequest.Records.LastOrDefault()?.Timestamp.ToUnixTimeMilliseconds() ?? -1;

    public static TestStream From(AppendToStreamRequest request) =>
        request.Checks.Length > 1
            ? throw new ArgumentException("Expected a single stream check", nameof(request))
            : new TestStream() {
                Stream        = request.Records[0].Stream,
                AppendRequest = request,
            };

    public IEnumerator<AppendRecord> GetEnumerator() => Records.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"{Stream} | recs: {Records.Count}";

    public static implicit operator AppendToStreamRequest(TestStream testStream) => testStream.AppendRequest;
}
