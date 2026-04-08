using Kurrent.Client;
using Kurrent.Client.Streams;
using Kurrent.Testing.Containers.KurrentDB;
using Testing.ArcadeRacer;

namespace Benchmarks.Infrastructure;

/// <summary>
/// Shared fixture for benchmarks that manages the KurrentDB container and client.
/// </summary>
public sealed class ClientBenchmarkFixture : IAsyncDisposable {
    readonly KurrentDBTestContainer      _container     = new();
    readonly Dictionary<int, StreamName> _seededStreams = new();

    KurrentClient? _client;

    public KurrentClient Client =>
        _client ?? throw new InvalidOperationException("Fixture not initialized. Call InitializeAsync first.");

    public ITestDataSource TestData { get; private set; } = null!;

    public async Task InitializeAsync() {
        await _container.InitializeAsync().ConfigureAwait(false);

        var connectionString = _container.AuthenticatedConnectionString;

        _client = KurrentClientOptions.Parse(connectionString).GetBuilder()
            .ConfigureSchema(KurrentClientSchemaOptions.NoValidation)
            .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
            .CreateClient();

        TestData = new ArcadeRacerTestDataSource();
    }

    /// <summary>
    /// Seeds a stream with the specified number of messages.
    /// Caches streams by message count to avoid re-seeding.
    /// </summary>
    public async Task<StreamName> SeedStream(int messageCount) {
        if (_seededStreams.TryGetValue(messageCount, out var existing))
            return existing;

        var data = await new ArcadeRacerTestDataSource().GenerateSet(messageCount, CancellationToken.None);

        await Client.Streams
            .AppendStream(AppendStreamRequest.For(data.Stream).WithRecords(data.Records).Build())
            .ConfigureAwait(false);

        _seededStreams[messageCount] = data.Stream;

        return data.Stream;
    }

    /// <summary>
    /// Gets all seeded streams for log-level benchmarks.
    /// </summary>
    public IReadOnlyDictionary<int, StreamName> SeededStreams => _seededStreams;

    public async ValueTask DisposeAsync() {
        if (_client is { })
            await _client.DisposeAsync().ConfigureAwait(false);

        await _container.DisposeAsync().ConfigureAwait(false);
    }
}
