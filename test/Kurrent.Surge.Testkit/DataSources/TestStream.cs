using System.Collections;
using System.Collections.Immutable;
using Kurrent.Client.Streams;

namespace Testing.ArcadeRacer;

public record TestStream : IEnumerable<AppendRecord> {
    TestStream() { }

    public StreamName Stream { get; private init; }

    public ImmutableArray<AppendRecord> Records { get; private init; } = [];

    public long LastTimestamp =>
        Records.LastOrDefault()?.Timestamp.ToUnixTimeMilliseconds() ?? -1;

    public static TestStream From(StreamName stream, ImmutableArray<AppendRecord> records) =>
        new() {
            Stream  = stream,
            Records = records,
        };

    public IEnumerator<AppendRecord> GetEnumerator() => ((IList<AppendRecord>)Records).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"{Stream} | recs: {Records.Length}";
}
