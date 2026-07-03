using System.Threading.Channels;
using Kurrent.Client;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Configuration;
using Kurrent.Streaming.Checkpointing;
using Kurrent.Streaming.Gateway;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Streaming;

#region ->> Control-plane events (child → commander) <<-

/// <summary>An event on the commander's single control channel. One consumer (the control loop) drains it,
/// so the Checkpointer's tracker is touched single-threaded — no locks, no shared mutable state.</summary>
abstract record CommanderEvent;

/// <summary>A checkpoint reached the commander — the Checkpointer's interval timer raised one, or the
/// subscription emitted a server watermark. The control loop folds the watermark and commits.</summary>
sealed record CheckpointReached(Checkpoint Checkpoint) : CommanderEvent;

/// <summary>A child's processed frontier for its partition, reported after each batch. The control loop
/// tracks it into the one checkpointer, which is what makes the interval timer fire.</summary>
sealed record ChildProgress(int Partition, RecordPosition Frontier) : CommanderEvent;

sealed record ChildStopRequested(int Partition, string? Reason)       : CommanderEvent;
sealed record ChildCheckpointRequested(int Partition, string? Reason) : CommanderEvent;
sealed record ChildFaulted(int Partition, Exception Error)            : CommanderEvent;
sealed record ChildCompleted(int Partition)                           : CommanderEvent;

#endregion

/// <summary>
/// EXPERIMENTAL — local partition parallelism for the Processor world. Receives the settings, owns ONE
/// subscription and ONE <see cref="Checkpointer"/> (matching a single <see cref="Drone"/> at
/// MaxParallelism &gt; 1), and creates one channel + one <see cref="ChildDrone"/> per partition. It reads the
/// partition-stamped subscription once and routes each record to the drone owning its partition.
/// <para>
/// The <see cref="Checkpointer"/> does the checkpointing: its interval timer raises checkpoints into the
/// ingest channel, the commander reads them and commits one event carrying every partition's position to
/// <c>$processors/{id}/checkpoints</c>. Children process and report their frontier; the commander tracks it
/// into the checkpointer (so the timer fires) and commits — children never commit.
/// </para>
/// </summary>
/// <remarks>
/// OPEN / UNRESOLVED — v1 simplifications, kept honest:
/// <list type="number">
/// <item><b>No flush alignment.</b> The commit reflects the frontier reported before it; it does not wait for
/// a per-child <c>OnCheckpoint</c>. <c>OnCheckpoint</c> / <c>OnConnected</c> are not invoked under the
/// commander yet (children receive only records). At-least-once; strict 2PC is out of scope.</item>
/// <item><b>Head-of-line blocking.</b> One subscription → N bounded child channels (Wait). A slow drone
/// stalls the shared reader. Pluggable back-pressure and the dead-letter strategy are later knobs.</item>
/// <item><b>Partition key.</b> Records route on the partition <see cref="KdbDataSource"/> stamps.</item>
/// </list>
/// </remarks>
sealed class DroneCommander(ProcessorOptions options, Func<IProcessor> processorFactory) {
    public async Task RunAsync(CancellationToken stoppingToken) {
        options.EnsureValid();

        var clientOptions = options.ClientOptions
                         ?? throw new InvalidOperationException("No connection configured. Call opts.Connection(...) first.");

        var client  = new KurrentClient(clientOptions);
        var streams = new StreamsClient(client);
        var maxP    = options.MaxParallelism;

        var partitions = Enumerable.Range(0, maxP).Select(Partition.From).ToArray();

        var store = new KurrentDBCheckpointStore(
            options.ProcessorId, maxP, options.Checkpoint.RetentionCount,
            streams, TimeProvider.System, NullLogger<KurrentDBCheckpointStore>.Instance);

        var bufferCapacity = options.Batching.MaxBatchSize * options.Batching.BufferMultiplier;

        // Ingest carries the subscription's records + server checkpoints AND the Checkpointer's interval /
        // manual checkpoints — two writers, exactly like a single Drone's channel.
        var ingest = Channel.CreateBounded<DataSourceMessage>(
            new BoundedChannelOptions(bufferCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });

        var dataSource = new KdbDataSource(
            new KdbDataSourceOptions {
                Filter               = options.Subscription.Filter,
                SkipDecoding         = options.SkipDecoding,
                PartitionKeySelector = options.Subscription.PartitionKeySelector,
                MaxParallelism       = maxP,
                ResiliencePipeline   = options.BuildResiliencePipeline(options.ClientOptions.LoggerFactory)
            },
            streams,
            options.ClientOptions.LoggerFactory);
        var checkpointer = new Checkpointer(options.DroneId, options.Checkpoint, store, ingest.Writer, TimeProvider.System);

        var control = Channel.CreateUnbounded<CommanderEvent>(new UnboundedChannelOptions { SingleReader = true });

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        // Load the aggregated floors and start the interval timer (gated on AutoCheckpoint, inside Initialize).
        var startPositions   = await checkpointer.Initialize(partitions, options.Subscription.InitialPosition, shutdown.Token).ConfigureAwait(false);
        var floorByPartition = startPositions.ToDictionary(p => p.Partition);

        // One channel + one drone per partition. Each child channel has a single writer (the demux).
        var childChannels = new Channel<DataSourceMessage>[maxP];
        var childTasks    = new Task[maxP];

        for (var i = 0; i < maxP; i++) {
            var channel = Channel.CreateBounded<DataSourceMessage>(
                new BoundedChannelOptions(bufferCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true });

            childChannels[i] = channel;

            var floor = floorByPartition.TryGetValue(Partition.From(i), out var f) ? f : RecordPosition.Earliest;

            var child = new ChildDrone(
                options, Partition.From(i), floor, channel, control.Writer,
                client.Options.LoggerFactory, TimeProvider.System, processorFactory());

            childTasks[i] = child.RunAsync(shutdown.Token);
        }

        var faults = new List<Exception>();

        try {
            var controlling = ControlLoop();
            var subscribing = dataSource.Connect(ingest.Writer, startPositions, shutdown.Token);

            await Demux().ConfigureAwait(false);
            CompleteChildChannels();

            await controlling.ConfigureAwait(false);            // ends when every child has completed
            await Task.WhenAll(childTasks).ConfigureAwait(false);

            await shutdown.CancelAsync().ConfigureAwait(false); // stop the subscription + timer
            await subscribing.ConfigureAwait(false);

            // Surface the outcome: a drone fault re-throws; external cancellation throws; a user stop returns.
            if (faults.Count > 0)
                throw new AggregateException("Fatal error in commander drone fleet", faults).Flatten();

            stoppingToken.ThrowIfCancellationRequested();
        }
        finally {
            await checkpointer.DisposeAsync().ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
        }

        // ── local functions ──────────────────────────────────────────────────────────────────────────

        // Read the partition-stamped subscription ONCE: route records to the owning child; hand a checkpoint
        // (interval timer or server watermark) to the control loop, which is the only thing that commits.
        async Task Demux() {
            try {
                await foreach (var message in ingest.Reader.ReadAllAsync(shutdown.Token).ConfigureAwait(false)) {
                    switch (message.Value) {
                        case Record record:
                            await Offer(childChannels[record.Position.Partition.Value].Writer, message).ConfigureAwait(false);
                            break;

                        case Connected:
                            // A flush marker for the children's batched read — keeps partial batches moving.
                            await Broadcast(message).ConfigureAwait(false);
                            break;

                        case Checkpoint checkpoint:
                            // Flush every child's pending batch (ReadBatched flushes on this marker), then
                            // commit the frontier the children have reported.
                            await Broadcast(message).ConfigureAwait(false);
                            control.Writer.TryWrite(new CheckpointReached(checkpoint));
                            break;
                    }
                }
            }
            catch (OperationCanceledException) {
                // Torn down — teardown completes the child channels so the children drain and exit.
            }
        }

        // Single consumer of the control channel — the only thing that touches the checkpointer's tracker,
        // so tracking and committing are single-threaded with no locks.
        async Task ControlLoop() {
            var completed    = 0;
            var shuttingDown = false;

            await foreach (var ev in control.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false)) {
                switch (ev) {
                    case ChildProgress(_, var frontier):
                        checkpointer.Track(frontier);   // feeds the tracker so the interval timer fires
                        break;

                    case CheckpointReached(var checkpoint):
                        // Commit the frontier the children have reported — one event carrying every partition's
                        // position. We deliberately do NOT fold the server scan watermark: the commander's
                        // tracker is fed only by per-partition child progress, and folding a watermark ahead of
                        // a child's progress would violate the tracker's strict monotonicity (it throws).
                        // Skipping the fold is at-least-once-safe — resume simply re-scans no-match regions.
                        var positions = checkpointer.PendingPositions();
                        if (positions.Length > 0)
                            await checkpointer.CommitCheckpoint(checkpoint.Id, positions, CancellationToken.None).ConfigureAwait(false);
                        break;

                    case ChildCheckpointRequested(_, var reason):
                        if (!shuttingDown)
                            await checkpointer.RaiseCheckpoint(CheckpointTrigger.Manual, reason).ConfigureAwait(false);
                        break;

                    case ChildStopRequested:
                        shuttingDown = true;
                        await Teardown().ConfigureAwait(false);
                        break;

                    case ChildFaulted(_, var error):
                        faults.Add(error);
                        shuttingDown = true;
                        await Teardown().ConfigureAwait(false);
                        break;

                    case ChildCompleted:
                        if (++completed >= maxP)
                            return;
                        break;
                }
            }
        }

        async Task Teardown() {
            if (!shutdown.IsCancellationRequested)
                await shutdown.CancelAsync().ConfigureAwait(false);
            CompleteChildChannels();
        }

        void CompleteChildChannels() {
            foreach (var channel in childChannels)
                channel.Writer.TryComplete();
        }

        async ValueTask Broadcast(DataSourceMessage message) {
            foreach (var channel in childChannels)
                await Offer(channel.Writer, message).ConfigureAwait(false);
        }

        // A write to a stopped/completed child channel during teardown is expected; bounded + Wait blocks.
        static async ValueTask Offer(ChannelWriter<DataSourceMessage> writer, DataSourceMessage message) {
            try {
                await writer.WriteAsync(message, CancellationToken.None).ConfigureAwait(false);
            }
            catch (ChannelClosedException) { }
        }
    }
}
