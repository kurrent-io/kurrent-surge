using System.Diagnostics.CodeAnalysis;
using Bogus;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Testkit;
using Testing.ArcadeRacer;

namespace Benchmarks.Infrastructure;

public sealed class ClientBenchmarkFixture : KurrentDBFixture {
    [SetsRequiredMembers]
    public ClientBenchmarkFixture() {
        Faker    = new BogusFaker();
        TestData = new ArcadeRacerTestDataSource();
    }
    
    readonly Dictionary<int, StreamName> _seededStreams = new();
 
    /// <summary>
    /// Seeds a stream with the specified number of messages.
    /// Caches streams by message count to avoid re-seeding.
    /// </summary>
    public async Task<StreamName> SeedStream(int messageCount) {
        if (_seededStreams.TryGetValue(messageCount, out var existing))
            return existing;

        var data = await TestData.GenerateSet(messageCount, CancellationToken.None);
        
        await Client.Streams
            .AppendStream(data.Stream, data.Records)
            .ConfigureAwait(false);

        _seededStreams[messageCount] = data.Stream;

        return data.Stream;
    }

    /// <summary>
    /// Seeds multiple distinct streams, each with the specified number of messages.
    /// Does not use the cache — each call produces unique streams.
    /// </summary>
    public async Task<StreamName[]> SeedStreams(int streamCount, int messageCount) {
        var streams = new StreamName[streamCount];

        for (var i = 0; i < streamCount; i++) {
            var data = await TestData.GenerateSet(messageCount, CancellationToken.None);

            await Client.Streams
                .AppendStream(data.Stream, data.Records)
                .ConfigureAwait(false);

            streams[i] = data.Stream;
        }

        return streams;
    }

    /// <summary>
    /// Gets all seeded streams for log-level benchmarks.
    /// </summary>
    public IReadOnlyDictionary<int, StreamName> SeededStreams => _seededStreams;
}
