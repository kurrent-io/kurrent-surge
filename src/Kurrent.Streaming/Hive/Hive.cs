using System.Threading.Channels;
using Kurrent.Client;
using Kurrent.Client.Processors.Partitioning;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Checkpointing;
using Kurrent.Streaming.Gateway;
using Kurrent.Streaming.Partitioning;

namespace Kurrent.Streaming.Hive;

/// <summary>
/// Runs N <see cref="IDrone"/>s in parallel, each owning a partition slice, against a single
/// shared gRPC subscription (the <see cref="MasterSubscriptionSource"/>).
/// </summary>
/// <remarks>
/// <para>
/// A Hive is a plain process, not a hosted service. Call <see cref="RunAsync"/> to start and
/// cancel the token to stop — cancelling cascades through the master subscription and each drone.
/// </para>
/// <para>
/// Partition assignment is static for v1: <see cref="PartitionAssigner"/> runs once at startup.
/// No leases, no locks, no rebalancing.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class Hive : IAsyncDisposable {
    readonly HiveOptions _options;

    readonly List<LocalPartitionedSource> _sources = [];
    readonly List<IDrone>                 _drones  = [];

    MasterSubscriptionSource? _master;
    ICheckpointStore?          _checkpointStore;
    KurrentClient?            _client;
    int                       _started;

    public Hive(HiveOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.DroneCount, 1);

        _options = options;
    }

    /// <summary>
    /// Materialized drones (empty until <see cref="RunAsync"/> is called).
    /// </summary>
    public IReadOnlyList<IDrone> Drones => _drones;

    public async Task RunAsync(CancellationToken ct = default) {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException(
                "This hive has already been started. Create a new instance to run again.");

        var processorOptions = _options.ProcessorOptions;

        if (_options.DroneCount > processorOptions.MaxParallelism)
            throw new InvalidOperationException(
                $"DroneCount ({_options.DroneCount}) cannot exceed MaxParallelism ({processorOptions.MaxParallelism}).");

        var processorTemplate = _options.ProcessorFactory();
        if (string.IsNullOrWhiteSpace(processorOptions.ProcessorId))
            processorOptions.ProcessorId = processorTemplate.GetType().Name.ToKebabCase();

        processorOptions.EnsureValid();

        _client = new KurrentClient(processorOptions.ClientOptions!);

        _checkpointStore = new KurrentDBCheckpointStore(
            processorOptions.ProcessorId,
            processorOptions.MaxParallelism,
            processorOptions.Checkpoint.RetentionCount,
            _client.Streams,
            _client.Options.Schema.Mapper,
            TimeProvider.System, 
            NullLogger<KurrentDBCheckpointStore>.Instance);

        var droneIds = Enumerable.Range(0, _options.DroneCount)
            .Select(_ => $"{processorOptions.ProcessorId}-{Guid.NewGuid().ToString("N")[^8..]}")
            .ToArray();

        var manifest = _options.Assigner.Distribute(
            DistributionManifest.Empty, droneIds, processorOptions.MaxParallelism);

        _master = new MasterSubscriptionSource(
            _client, processorOptions, _checkpointStore, TimeProvider.System);

        foreach (var droneId in droneIds) {
            var partitions = manifest[droneId].All;

            var channel = Channel.CreateBounded<SubscribeMessage>(
                new BoundedChannelOptions(processorOptions.BufferCapacity) {
                    FullMode     = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false
                });

            var checkpointer = new Checkpointer(
                droneId, processorOptions.Checkpoint,
                _checkpointStore, channel.Writer, TimeProvider.System);

            var source = new LocalPartitionedSource(
                _master, droneId, partitions, processorOptions,
                _checkpointStore, channel.Writer, TimeProvider.System);
            _sources.Add(source);

            var droneOptions = processorOptions with { DroneId = droneId };
            var droneProcessor = droneId == droneIds[0] ? processorTemplate : _options.ProcessorFactory();

            var drone = new Drone(
                droneOptions, source, checkpointer, channel,
                new StreamsClient(_client), _client.Options.LoggerFactory,
                TimeProvider.System, droneProcessor);

            _drones.Add(drone);
        }

        await _master.InitializeAsync(ct).ConfigureAwait(false);

        var allTasks = new List<Task> { _master.RunAsync(ct) };
        allTasks.AddRange(_drones.Select(d => d.RunAsync(ct)));

        try {
            await Task.WhenAll(allTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync() {
        try {
            foreach (var source in _sources) {
                try { await source.DisposeAsync().ConfigureAwait(false); }
                catch { }
            }

            if (_master is not null)
                try { await _master.DisposeAsync().ConfigureAwait(false); }
                catch { }
        }
        finally {
            if (_client is not null)
                await _client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
