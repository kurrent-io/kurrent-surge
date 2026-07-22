using System.Diagnostics;
using System.Diagnostics.Metrics;
using Kurrent.Diagnostics;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Diagnostics;
using Kurrent.Surge.Streaming.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace Kurrent.Surge.Streaming.Tests;

// ActivityListener registration is process-global and the ActivitySource is shared, so tests that
// depend on listener presence or absence must not overlap with others in the same group.
[Category("Processors")]
[NotInParallel("ActivitySourceListeners")]
public class ProcessorTelemetryObserverTests {
    static readonly RecordFaker Records = new();
    static readonly Bogus.Faker Faker   = new();

    static ProcessorInfo NewInfo() => new(Faker.Random.AlphaNumeric(10), Faker.Random.AlphaNumeric(10));

    static DroneProgress ProgressWithUncommitted(long uncommittedRecords) =>
        DroneProgress.Empty with { UncommittedRecords = uncommittedRecords };

    /// <summary>
    /// Arranges an upstream context the way a real producer would: a live Activity injected into
    /// the record's metadata through the SDK's propagation extensions. Stopped before returning
    /// so it never lingers as the ambient parent of the span under test.
    /// </summary>
    static Activity InjectProducerContext(Metadata metadata) {
        var producer = new Activity("producer").Start();
        metadata.InjectTraceContext(producer);
        producer.Stop();
        return producer;
    }

    /// <summary>
    /// Captures all four telemetry channels produced by one composite observer, isolated to a single
    /// test's processor id: logs via <see cref="CapturingLoggerFactory"/>, metrics via a
    /// factory-scoped <see cref="MeterListener"/>, spans via an <see cref="ActivityListener"/>, and
    /// bus events via a <see cref="TelemetryConstants.Listener"/> subscription.
    /// </summary>
    sealed class TelemetryProbe : IDisposable {
        public TelemetryProbe(ProcessorInfo info, bool listenActivities = true) {
            Observer = new ProcessorTelemetryObserver(Logs.CreateLogger(nameof(ProcessorTelemetryObserver)), _meterFactory);

            _meterListener = new MeterListener {
                InstrumentPublished = (instrument, listener) => {
                    if (_meterFactory.Meters.Contains(instrument.Meter))
                        listener.EnableMeasurementEvents(instrument);
                }
            };

            _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
            _meterListener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Add(instrument, value, tags));
            _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
            _meterListener.Start();

            if (listenActivities) {
                _activityListener = new ActivityListener {
                    ShouldListenTo  = source => source.Name == TelemetryConstants.SourceName,
                    Sample          = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStopped = activity => {
                        if (Equals(activity.GetTagItem("processor.id"), info.ProcessorId.Value))
                            Spans.Add(activity);
                    }
                };

                ActivitySource.AddActivityListener(_activityListener);
            }

            _busSubscription = TelemetryConstants.Listener.Subscribe(new BusCollector(info, BusEvents));
        }

        readonly TestMeterFactory  _meterFactory = new();
        readonly MeterListener     _meterListener;
        readonly ActivityListener? _activityListener;
        readonly IDisposable       _busSubscription;

        public ProcessorTelemetryObserver Observer { get; }

        public CapturingLoggerFactory Logs  { get; } = new();
        public List<Activity>         Spans { get; } = [];

        public List<(string Name, double Value, Dictionary<string, object?> Tags)> Measurements { get; } = [];
        public List<(string Name, object? Payload)>                                BusEvents    { get; } = [];

        void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) {
            var map = new Dictionary<string, object?>();
            foreach (var tag in tags)
                map[tag.Key] = tag.Value;

            Measurements.Add((instrument.Name, value, map));
        }

        public double Total(string instrument) =>
            Measurements.Where(m => m.Name == instrument).Sum(m => m.Value);

        public List<(double Value, Dictionary<string, object?> Tags)> Of(string instrument) =>
            Measurements.Where(m => m.Name == instrument).Select(m => (m.Value, m.Tags)).ToList();

        public string BusNames => string.Join(",", BusEvents.Select(evt => evt.Name));

        public void Dispose() {
            _busSubscription.Dispose();
            _activityListener?.Dispose();
            _meterListener.Dispose();
            _meterFactory.Dispose();
        }

        sealed class BusCollector(ProcessorInfo info, List<(string, object?)> events) : IObserver<KeyValuePair<string, object?>> {
            public void OnNext(KeyValuePair<string, object?> evt) {
                var payloadInfo = evt.Value switch {
                    ProcessorStateChanged e => e.Info,
                    RecordSkipped e         => e.Info,
                    RecordReady e           => e.Info,
                    RecordCompleted e       => e.Info,
                    BatchReady e            => e.Info,
                    BatchCompleted e        => e.Info,
                    CheckpointCommitted e   => e.Info,
                    CheckpointFailed e      => e.Info,
                    _                       => (ProcessorInfo?)null
                };

                if (payloadInfo == info)
                    events.Add((evt.Key, evt.Value));
            }

            public void OnCompleted()            { }
            public void OnError(Exception error) { }
        }
    }

    #region ->> Cross-channel wiring <<-

    [Test]
    public async ValueTask processed_record_emits_all_four_telemetry_channels() {
        // Arrange
        var info               = NewInfo();
        var record             = Records.Generate();
        var elapsed            = TimeSpan.FromMilliseconds(Faker.Random.Int(10, 500));
        var uncommittedRecords = Faker.Random.Long(1, 1_000);
        var now                = DateTimeOffset.UtcNow;

        using var probe = new TelemetryProbe(info);

        // Act
        probe.Observer.OnRecordReady(new(info, record, now));
        probe.Observer.OnRecordCompleted(new(info, record, RecordOutcome.Processed, elapsed, ProgressWithUncommitted(uncommittedRecords), now));

        // Assert — one observer, four channels
        var log  = probe.Logs.Entries.Single();
        var span = probe.Spans.Single();

        await Assert.That(log.Level).IsEqualTo(LogLevel.Debug);
        await Assert.That(log.Message).StartsWith($"[{info.DroneId}] Record processed");
        await Assert.That(probe.Total("kurrent_processor_records_processed")).IsEqualTo(1d);
        await Assert.That(probe.Of("kurrent_processor_record_duration_seconds").Single().Value).IsEqualTo(elapsed.TotalSeconds);
        await Assert.That(probe.Of("kurrent_processor_uncommitted_records").Single().Value).IsEqualTo(uncommittedRecords);
        await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Ok);
        await Assert.That(span.Duration).IsEqualTo(elapsed);
        await Assert.That(probe.BusNames).IsEqualTo($"{nameof(RecordReady)},{nameof(RecordCompleted)}");
    }

    [Test]
    public async ValueTask published_payload_is_the_event_struct() {
        // Arrange
        var info   = NewInfo();
        var record = Records.Generate();
        var evt    = new RecordCompleted(info, record, RecordOutcome.Processed, TimeSpan.FromMilliseconds(10), DroneProgress.Empty, DateTimeOffset.UtcNow);

        using var probe = new TelemetryProbe(info, listenActivities: false);

        // Act
        probe.Observer.OnRecordCompleted(in evt);

        // Assert — the boxed payload round-trips as the original struct, record reference included
        var payload = probe.BusEvents.Single().Payload;

        await Assert.That(payload).IsTypeOf<RecordCompleted>();
        await Assert.That((RecordCompleted)payload!).IsEqualTo(evt);
    }

    #endregion // Cross-channel wiring

    #region ->> Logging <<-

    [Test]
    public async ValueTask state_transitions_log_at_expected_levels() {
        // Arrange
        var info      = NewInfo();
        var now       = DateTimeOffset.UtcNow;
        var sessionId = SessionId.From(Guid.NewGuid());

        var expected = string.Join(
            "|", [
                $"{LogLevel.Debug}:[{info.DroneId}] Starting",
                $"{LogLevel.Information}:[{info.DroneId}] Processing (sessionId: {sessionId})",
                $"{LogLevel.Information}:[{info.DroneId}] Stopped Gracefully"
            ]
        );

        using var probe = new TelemetryProbe(info, listenActivities: false);

        // Act
        probe.Observer.OnStateChanged(new(info, DroneStatus.Unspecified, DroneStatus.Starting, DroneProgress.Empty, now));
        probe.Observer.OnStateChanged(new(info, DroneStatus.Starting, DroneStatus.Processing, DroneProgress.Empty, now, SessionId: sessionId));
        probe.Observer.OnStateChanged(new(info, DroneStatus.Processing, DroneStatus.Stopped, DroneProgress.Empty, now));

        // Assert
        var actual = string.Join("|", probe.Logs.Entries.Select(entry => $"{entry.Level}:{entry.Message}"));

        await Assert.That(actual).IsEqualTo(expected);
        await Assert.That(probe.BusEvents.Count).IsEqualTo(3);
    }

    [Test]
    public async ValueTask record_skipped_logs_debug_and_publishes() {
        // Arrange
        var info     = NewInfo();
        var record   = Records.Generate();
        var expected = (LogLevel.Debug, $"[{info.DroneId}] Record skipped");

        using var probe = new TelemetryProbe(info, listenActivities: false);

        // Act
        probe.Observer.OnRecordSkipped(new(info, record, DateTimeOffset.UtcNow));

        // Assert
        await Assert.That(probe.Logs.Entries.Single()).IsEqualTo(expected);
        await Assert.That(probe.BusNames).IsEqualTo(nameof(RecordSkipped));
    }

    #endregion // Logging

    #region ->> Metrics <<-

    [Test]
    public async ValueTask error_transition_logs_and_increments_drone_errors() {
        // Arrange
        var info         = NewInfo();
        var errorMessage = Faker.Lorem.Sentence();
        var expectedLog  = (LogLevel.Error, $"[{info.DroneId}] Error: {errorMessage}");

        using var probe = new TelemetryProbe(info, listenActivities: false);

        // Act
        probe.Observer.OnStateChanged(new(
            info, DroneStatus.Processing, DroneStatus.Stopped, DroneProgress.Empty,
            DateTimeOffset.UtcNow, new InvalidOperationException(errorMessage)
        ));

        // Assert
        var errors = probe.Of("kurrent_processor_drone_errors").Single();

        await Assert.That(probe.Logs.Entries.Single()).IsEqualTo(expectedLog);
        await Assert.That(errors.Value).IsEqualTo(1d);
        await Assert.That(errors.Tags["processor_id"]).IsEqualTo(info.ProcessorId.Value);
    }

    [Test]
    public async ValueTask failed_record_emits_error_log_and_span_but_no_metrics() {
        // Arrange
        var info         = NewInfo();
        var record       = Records.Generate();
        var errorMessage = Faker.Lorem.Sentence();
        var expectedLog  = (LogLevel.Error, $"[{info.DroneId}] Record failed: {errorMessage}");
        var now          = DateTimeOffset.UtcNow;

        using var probe = new TelemetryProbe(info);

        // Act
        probe.Observer.OnRecordReady(new(info, record, now));
        probe.Observer.OnRecordCompleted(new(
            info, record, RecordOutcome.Failed, TimeSpan.FromMilliseconds(10),
            DroneProgress.Empty, now, new InvalidOperationException(errorMessage)
        ));

        // Assert
        var span = probe.Spans.Single();

        await Assert.That(probe.Logs.Entries.Single()).IsEqualTo(expectedLog);
        await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert.That(span.StatusDescription).IsEqualTo(errorMessage);
        await Assert.That(span.GetTagItem("error.type")).IsEqualTo(typeof(InvalidOperationException).FullName);
        await Assert.That(span.Events.Select(e => e.Name)).Contains("exception");
        await Assert.That(probe.Measurements).IsEmpty();
    }

    [Test]
    public async ValueTask processed_batch_counts_committed_prefix_once() {
        // Arrange — 3 dispatched, 2 committed at the frontier
        var info           = NewInfo();
        var batch          = Records.Generate(3);
        var processedCount = 2;
        var elapsed        = TimeSpan.FromMilliseconds(Faker.Random.Int(10, 500));

        var expectedPerRecordSeconds = elapsed.TotalSeconds / processedCount;

        using var probe = new TelemetryProbe(info, listenActivities: false);

        // Act
        probe.Observer.OnBatchCompleted(new(
            info, batch, RecordOutcome.Processed, processedCount, elapsed,
            DroneProgress.Empty, DateTimeOffset.UtcNow
        ));

        // Assert — the counter and batch_size carry the committed prefix, never the dispatched size
        await Assert.That(probe.Logs.Entries.Single().Message).StartsWith($"[{info.DroneId}] Batch processed (committed: {processedCount}");
        await Assert.That(probe.Total("kurrent_processor_records_processed")).IsEqualTo(processedCount);
        await Assert.That(probe.Of("kurrent_processor_batch_size").Single().Value).IsEqualTo(processedCount);
        await Assert.That(probe.Of("kurrent_processor_batch_duration_seconds").Single().Value).IsEqualTo(elapsed.TotalSeconds);
        await Assert.That(probe.Of("kurrent_processor_record_duration_seconds").Single().Value).IsEqualTo(expectedPerRecordSeconds);
    }

    [Test]
    public async ValueTask checkpoint_outcomes_count_by_trigger() {
        // Arrange
        var info         = NewInfo();
        var checkpointId = Faker.Random.AlphaNumeric(8);
        var trigger      = Faker.PickRandom(CheckpointTrigger.Interval, CheckpointTrigger.Rebalance);
        var duration     = TimeSpan.FromMilliseconds(Faker.Random.Int(5, 100));
        var errorMessage = Faker.Lorem.Sentence();

        var expectedTrigger   = trigger.ToString();
        var expectedFailedLog = (LogLevel.Error, $"[{info.DroneId}] Checkpoint {checkpointId} ({trigger}) failed: {errorMessage}");

        using var probe = new TelemetryProbe(info, listenActivities: false);

        // Act
        probe.Observer.OnCheckpointCommitted(new(info, trigger, checkpointId, [], duration, DateTimeOffset.UtcNow));
        probe.Observer.OnCheckpointFailed(new(info, trigger, checkpointId, [], new InvalidOperationException(errorMessage), DateTimeOffset.UtcNow));

        // Assert
        var completed = probe.Of("kurrent_processor_checkpoints_completed").Single();
        var failed    = probe.Of("kurrent_processor_checkpoints_failed").Single();

        await Assert.That(completed.Value).IsEqualTo(1d);
        await Assert.That(completed.Tags["trigger"]).IsEqualTo(expectedTrigger);
        await Assert.That(failed.Value).IsEqualTo(1d);
        await Assert.That(failed.Tags["trigger"]).IsEqualTo(expectedTrigger);
        await Assert.That(probe.Of("kurrent_processor_checkpoint_duration_seconds").Single().Value).IsEqualTo(duration.TotalSeconds);
        await Assert.That(probe.Logs.Entries.Last()).IsEqualTo(expectedFailedLog);
        await Assert.That(probe.BusNames).IsEqualTo($"{nameof(CheckpointCommitted)},{nameof(CheckpointFailed)}");
    }

    #endregion // Metrics

    #region ->> Tracing <<-

    [Test]
    public async ValueTask record_span_follows_messaging_semconv() {
        // Arrange
        var info    = NewInfo();
        var record  = Records.Generate();
        var elapsed = TimeSpan.FromMilliseconds(Faker.Random.Int(10, 500));
        var now     = DateTimeOffset.UtcNow;

        var expectedName = $"process {record.Stream}";

        using var probe = new TelemetryProbe(info);

        // Act
        probe.Observer.OnRecordReady(new(info, record, now));
        probe.Observer.OnRecordCompleted(new(info, record, RecordOutcome.Processed, elapsed, DroneProgress.Empty, now));

        // Assert
        var span = probe.Spans.Single();

        await Assert.That(span.OperationName).IsEqualTo(expectedName);
        await Assert.That(span.Kind).IsEqualTo(ActivityKind.Consumer);
        await Assert.That(span.GetTagItem("messaging.system")).IsEqualTo("kurrentdb");
        await Assert.That(span.GetTagItem("messaging.destination.name")).IsEqualTo(record.Stream.ToString());
        await Assert.That(span.GetTagItem("messaging.message.id")).IsEqualTo(record.Id.ToString());
        await Assert.That(span.GetTagItem("processor.drone_id")).IsEqualTo(info.DroneId);
    }

    [Test]
    public async ValueTask record_span_continues_the_trace_propagated_by_the_producer() {
        // Arrange — a real producer activity, injected through the propagation extensions
        var info     = NewInfo();
        var record   = Records.Generate();
        var producer = InjectProducerContext(record.Metadata);
        var now      = DateTimeOffset.UtcNow;

        using var probe = new TelemetryProbe(info);

        // Act
        probe.Observer.OnRecordReady(new(info, record, now));
        probe.Observer.OnRecordCompleted(new(info, record, RecordOutcome.Processed, TimeSpan.FromMilliseconds(10), DroneProgress.Empty, now));

        // Assert — same trace, parented to the producer's span
        var span = probe.Spans.Single();

        await Assert.That(span.TraceId).IsEqualTo(producer.TraceId);
        await Assert.That(span.ParentSpanId).IsEqualTo(producer.SpanId);
    }

    [Test]
    public async ValueTask cancelled_record_logs_and_marks_span_error() {
        // Arrange
        var info   = NewInfo();
        var record = Records.Generate();
        var now    = DateTimeOffset.UtcNow;

        var expectedLog = (LogLevel.Debug, $"[{info.DroneId}] Record processing cancelled");

        using var probe = new TelemetryProbe(info);

        // Act
        probe.Observer.OnRecordReady(new(info, record, now));
        probe.Observer.OnRecordCompleted(new(info, record, RecordOutcome.Cancelled, TimeSpan.FromMilliseconds(10), DroneProgress.Empty, now));

        // Assert
        var span = probe.Spans.Single();

        await Assert.That(probe.Logs.Entries.Single()).IsEqualTo(expectedLog);
        await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert.That(span.StatusDescription).IsEqualTo("Processing was cancelled");
    }

    [Test]
    public async ValueTask processing_span_context_round_trips_through_the_propagator() {
        // Arrange
        var info   = NewInfo();
        var record = Records.Generate();
        var now    = DateTimeOffset.UtcNow;

        using var probe = new TelemetryProbe(info);

        // Act
        probe.Observer.OnRecordReady(new(info, record, now));
        probe.Observer.OnRecordCompleted(new(info, record, RecordOutcome.Processed, TimeSpan.FromMilliseconds(10), DroneProgress.Empty, now));

        // Assert — the stamp happened at Ready; extracting through the SDK extensions yields
        // the processing span's context (what a downstream consumer would parent to)
        var span      = probe.Spans.Single();
        var extracted = record.Metadata.ExtractTraceContext();

        await Assert.That(extracted).IsNotNull();
        await Assert.That(extracted!.Value.TraceId).IsEqualTo(span.TraceId);
        await Assert.That(extracted.Value.SpanId).IsEqualTo(span.SpanId);
    }

    [Test]
    public async ValueTask batch_span_links_message_contexts_and_stamps_all_records() {
        // Arrange — one record in the batch carries a producer trace context, two do not
        var info  = NewInfo();
        var batch = Records.Generate(3);
        var now   = DateTimeOffset.UtcNow;

        var producer = InjectProducerContext(batch[0].Metadata);

        using var probe = new TelemetryProbe(info);

        // Act
        probe.Observer.OnBatchReady(new(info, batch, now));
        probe.Observer.OnBatchCompleted(new(info, batch, RecordOutcome.Processed, batch.Count, TimeSpan.FromMilliseconds(25), DroneProgress.Empty, now));

        // Assert
        var span = probe.Spans.Single();

        await Assert.That(span.OperationName).IsEqualTo("process");
        await Assert.That(span.GetTagItem("messaging.batch.message_count")).IsEqualTo(batch.Count);
        await Assert.That(span.Links.Count()).IsEqualTo(1);
        await Assert.That(span.Links.Single().Context.TraceId).IsEqualTo(producer.TraceId);

        foreach (var record in batch)
            await Assert.That(record.Metadata.ExtractTraceContext()?.SpanId).IsEqualTo(span.SpanId);
    }

    [Test]
    public async ValueTask no_activity_listener_produces_no_span_and_no_metadata_stamp() {
        // Arrange — no activity listener registered for the source in this serialized group
        var info   = NewInfo();
        var record = Records.Generate();
        var now    = DateTimeOffset.UtcNow;

        using var probe = new TelemetryProbe(info, listenActivities: false);

        // Act
        probe.Observer.OnRecordReady(new(info, record, now));
        probe.Observer.OnRecordCompleted(new(info, record, RecordOutcome.Processed, TimeSpan.FromMilliseconds(10), DroneProgress.Empty, now));

        // Assert
        await Assert.That(record.Metadata.ExtractTraceContext()).IsNull();
        await Assert.That(probe.Spans).IsEmpty();
    }

    #endregion // Tracing
}
