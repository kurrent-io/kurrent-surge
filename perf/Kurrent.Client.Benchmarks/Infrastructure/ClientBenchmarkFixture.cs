using System.Diagnostics.CodeAnalysis;
using Bogus;
using Kurrent.Client.Streams;
using Kurrent.Sdk.Testkit;
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

        var data = await new ArcadeRacerTestDataSource().GenerateSet(messageCount, CancellationToken.None);

        await Client.Streams
            .AppendStream(AppendStreamRequest.For(data.Stream).AddRecords(data.Records).Build())
            .ConfigureAwait(false);

        _seededStreams[messageCount] = data.Stream;

        return data.Stream;
    }

    /// <summary>
    /// Gets all seeded streams for log-level benchmarks.
    /// </summary>
    public IReadOnlyDictionary<int, StreamName> SeededStreams => _seededStreams;
}
