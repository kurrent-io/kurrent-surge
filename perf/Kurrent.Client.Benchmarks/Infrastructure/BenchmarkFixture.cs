using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Tools;
using Kurrent.Testing.Containers.KurrentDB;

namespace Kurrent.Client.Benchmarks.Infrastructure;

/// <summary>
/// Shared fixture for benchmarks that manages the KurrentDB container and client.
/// </summary>
public sealed class BenchmarkFixture : IAsyncDisposable {
    readonly KurrentDBTestContainer _container;
    readonly Dictionary<int, StreamName> _seededStreams = new();

    KurrentClient? _client;

    public BenchmarkFixture() {
        _container = new KurrentDBTestContainer();
    }

    public KurrentClient Client =>
        _client ?? throw new InvalidOperationException("Fixture not initialized. Call InitializeAsync first.");

    public async Task InitializeAsync() {
        await _container.InitializeAsync().ConfigureAwait(false);

        var connectionString = _container.AuthenticatedConnectionString;

        _client = KurrentClientOptions.Parse(connectionString).GetBuilder()
            .ConfigureSchema(KurrentClientSchemaOptions.NoValidation)
            .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
            .CreateClient();
    }

    /// <summary>
    /// Seeds a stream with the specified number of messages.
    /// Caches streams by message count to avoid re-seeding.
    /// </summary>
    public async Task<StreamName> SeedStream(int messageCount) {
        if (_seededStreams.TryGetValue(messageCount, out var existing))
            return existing;

        var activity = HomeAutomationTestData.SimulateHomeActivity(messageCount);

        await Client.Streams
            .AppendRecords(activity.Stream, ExpectedStreamState.Any, activity.Records)
            .ConfigureAwait(false);

        _seededStreams[messageCount] = activity.Stream;

        return activity.Stream;
    }

    /// <summary>
    /// Gets all seeded streams for log-level benchmarks.
    /// </summary>
    public IReadOnlyDictionary<int, StreamName> SeededStreams => _seededStreams;

    public async ValueTask DisposeAsync() {
        if (_client is not null)
            await _client.DisposeAsync().ConfigureAwait(false);

        await _container.DisposeAsync().ConfigureAwait(false);
    }
}
