using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors.Checkpointing;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Checkpointing;
using Kurrent.Surge.Processors.Gateway;
using Kurrent.Surge.Processors.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Benchmarks;

/// <summary>
/// Drone dispatch throughput — drives a real <see cref="Drone"/> over an in-memory channel with no-op
/// processors, so the measurement isolates the framework's read/dispatch/track cost (not user work).
/// Compares the two dispatch shapes (per-record vs whole-batch) across the batch-size knob the
/// "batched read as the core primitive" refactor introduced.
/// </summary>
/// <remarks>
/// The drone is single-use (<c>RunAsync</c> guards against re-entry), so each invocation builds a fresh
/// channel + checkpointer + data source + drone. That wiring is cheap and identical across arms, so the
/// per-record vs batch and batch-size comparisons stay valid; record generation is hoisted to setup.
/// The channel is bounded exactly as production wires it (capacity = MaxBatchSize × BufferMultiplier), so
/// the size-1 case pays the real backpressure cost. AutoCheckpoint is off to keep the interval timer out of
/// the measurement; AutoTrack stays on so per-record/per-batch tracking is included.
/// </remarks>
[MemoryDiagnoser(displayGenColumns: false), Config(typeof(BenchmarkConfig))]
[BenchmarkCategory("Processors")]
public class DroneDispatchBenchmarks {
    const int BufferMultiplier = 2;

    [Params(1_000, 10_000)]
    public int RecordCount { get; set; }

    [Params(1, 100, 500)]
    public int MaxBatchSize { get; set; }

    Record[] _records   = null!;
    Connected _connected;

    [GlobalSetup]
    public void Setup() {
        _connected = new Connected(SessionId.From(Guid.CreateVersion7()));

        // Single partition (MaxParallelism = 1). Log positions start at 1 so the Earliest catch-up
        // floor (the in-memory store) never filters them.
        _records = new Record[RecordCount];
        for (var i = 0; i < RecordCount; i++)
            _records[i] = new Record {
                Position = RecordPosition.From(LogPosition.From(i + 1), Partition.From(0))
            };
    }

    [Benchmark(Baseline = true)]
    public ValueTask<long> PerRecord() => RunDrone(new NoopRecordProcessor());

    [Benchmark]
    public ValueTask<long> Batch() => RunDrone(new NoopBatchProcessor());

    async ValueTask<long> RunDrone(IProcessor processor) {
        var channel = Channel.CreateBounded<DataSourceMessage>(
            new BoundedChannelOptions(MaxBatchSize * BufferMultiplier) {
                FullMode     = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        var options = new ProcessorOptions { ProcessorId = "bench" };
        options.Batching.MaxBatchSize     = MaxBatchSize;
        options.Checkpoint.AutoCheckpoint = false;

        var checkpointer = new Checkpointer(
            options.DroneId, options.Checkpoint, new BenchCheckpointStore(),
            channel.Writer, TimeProvider.System);

        var dataSource = new BenchDataSource(_connected, _records);

        // No observers: the benchmark measures dispatch, not telemetry. Registering the composite here
        // would fold logging, metrics and span creation into the numbers.
        var drone = new Drone(
            options, dataSource, checkpointer, channel,
            streams: null!, NullLoggerFactory.Instance, TimeProvider.System, processor,
            observers: []);

        await drone.RunAsync(CancellationToken.None).ConfigureAwait(false);

        return drone.Progress.TotalRecords;
    }
}

sealed class NoopRecordProcessor : IRecordProcessor {
    public ValueTask ProcessRecord(ProcessRecordContext context) => ValueTask.CompletedTask;
}

sealed class NoopBatchProcessor : IRecordBatchProcessor {
    public ValueTask ProcessBatch(BatchContext context) => ValueTask.CompletedTask;
}

/// <summary>In-memory data source: dumps a pre-built Connected + record set into the drone's channel.</summary>
sealed class BenchDataSource(Connected connected, Record[] records) : IDataSource {
    public async Task Connect(ChannelWriter<DataSourceMessage> writer, RecordPosition[] startPositions, CancellationToken ct) {
        try {
            await writer.WriteAsync(connected, ct).ConfigureAwait(false);

            foreach (var record in records)
                await writer.WriteAsync(record, ct).ConfigureAwait(false);

            writer.TryComplete();
        }
        catch (Exception ex) {
            writer.TryComplete(ex);
        }
    }
}

/// <summary>In-memory checkpoint store: floors every partition at Earliest, persists nothing.</summary>
sealed class BenchCheckpointStore : ICheckpointStore {
    public ValueTask<ProcessorCheckpoint> LoadLatestAsync(string droneId, CancellationToken ct = default) =>
        new(ProcessorCheckpoint.Empty);

    public ValueTask<ProcessorCheckpoint> LoadLatestAsync(
        string droneId, Partition[] partitions,
        SubscriptionInitialPosition initialPosition, CancellationToken ct = default
    ) {
        var positions = partitions
            .Select(p => RecordPosition.Earliest with { Partition = p })
            .ToArray();

        return new(new ProcessorCheckpoint { Positions = positions });
    }

    public ValueTask StoreAsync(string droneId, string checkpointId, RecordPosition[] positions, CancellationToken ct = default) =>
        ValueTask.CompletedTask;

    public ValueTask<ProcessorCheckpoint> ResetAsync(string droneId, string checkpointId, LogPosition position, CancellationToken ct = default) =>
        new(ProcessorCheckpoint.Empty);
}
