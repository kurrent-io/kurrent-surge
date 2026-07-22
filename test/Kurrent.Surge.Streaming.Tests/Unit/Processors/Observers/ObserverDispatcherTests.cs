using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Surge.Streaming.Tests;

[Category("Processors")]
public class ObserverDispatcherTests {
    static readonly RecordFaker Records = new();
    static readonly Bogus.Faker Faker   = new();

    static readonly ProcessorInfo Info = new(Faker.Random.AlphaNumeric(10), Faker.Random.AlphaNumeric(10));

    static void DispatchAllEvents(ObserverDispatcher dispatcher, Record record, IReadOnlyList<Record> batch) {
        var now = DateTimeOffset.UtcNow;

        dispatcher.StateChanged(new(Info, DroneStatus.Starting, DroneStatus.Processing, now, SessionId: Guid.NewGuid()));
        dispatcher.RecordSkipped(new(Info, record, now));
        dispatcher.RecordReady(new(Info, record, now));
        dispatcher.RecordCompleted(new(Info, record, RecordOutcome.Processed, TimeSpan.Zero, DroneProgress.Empty, now));
        dispatcher.BatchReady(new(Info, batch, now));
        dispatcher.BatchCompleted(new(Info, batch, RecordOutcome.Processed, batch.Count, TimeSpan.Zero, DroneProgress.Empty, now));
        dispatcher.CheckpointCommitted(new(Info, CheckpointTrigger.Interval, Faker.Random.AlphaNumeric(8), [], TimeSpan.Zero, now));
        dispatcher.CheckpointFailed(new(Info, CheckpointTrigger.Interval, Faker.Random.AlphaNumeric(8), [], new InvalidOperationException(), now));
    }

    static List<string> ExpectedSequence(params string[] observerNames) =>
        new[] {
            nameof(ProcessorStateChanged),
            nameof(RecordSkipped),
            nameof(RecordReady),
            nameof(RecordCompleted),
            nameof(BatchReady),
            nameof(BatchCompleted),
            nameof(CheckpointCommitted),
            nameof(CheckpointFailed)
        }
        .SelectMany(evt => observerNames.Select(name => $"{name}:{evt}"))
        .ToList();

    [Test]
    public async ValueTask dispatches_every_event_to_all_observers_in_registration_order() {
        // Arrange
        var log      = new List<string>();
        var record   = Records.Generate();
        var batch    = Records.Generate(3);
        var expected = string.Join(",", ExpectedSequence("first", "second"));

        var dispatcher = new ObserverDispatcher(
            [
                new TestObserver(evt => log.Add($"first:{evt.GetType().Name}")),
                new TestObserver(evt => log.Add($"second:{evt.GetType().Name}"))
            ],
            NullLogger.Instance
        );

        // Act
        DispatchAllEvents(dispatcher, record, batch);

        // Assert
        await Assert.That(string.Join(",", log)).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask observer_failure_does_not_stop_remaining_observers() {
        // Arrange
        var log      = new List<string>();
        var record   = Records.Generate();
        var batch    = Records.Generate(3);
        var expected = string.Join(",", ExpectedSequence("survivor"));

        var dispatcher = new ObserverDispatcher(
            [
                new TestObserver(evt => throw new InvalidOperationException(evt.GetType().Name)),
                new TestObserver(evt => log.Add($"survivor:{evt.GetType().Name}"))
            ],
            NullLogger.Instance
        );

        // Act — the throwing observer fails on every hook; dispatch must reach the survivor each time
        DispatchAllEvents(dispatcher, record, batch);

        // Assert
        await Assert.That(string.Join(",", log)).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask metadata_mutation_in_record_ready_is_visible_to_subsequent_observers() {
        // Arrange
        var record        = Records.Generate();
        var metadataKey   = Faker.Random.AlphaNumeric(12);
        var metadataValue = Faker.Random.AlphaNumeric(12);
        var seenValues    = new List<object?>();

        var dispatcher = new ObserverDispatcher(
            [
                TestObserver.For<RecordReady>(evt => evt.Record.Metadata[metadataKey] = metadataValue),
                TestObserver.For<RecordReady>(evt => seenValues.Add(evt.Record.Metadata.FirstOrDefault(kvp => kvp.Key == metadataKey).Value))
            ],
            NullLogger.Instance
        );

        // Act
        dispatcher.RecordReady(new(Info, record, DateTimeOffset.UtcNow));

        // Assert — the second observer saw the stamp, and it survives dispatch (the handler would see it)
        await Assert.That(seenValues).Contains(metadataValue);
        await Assert.That(record.Metadata).Contains(new KeyValuePair<string, object?>(metadataKey, metadataValue));
    }
}

/// <summary>
/// Single delegating observer for tests: every hook forwards the (boxed) event to one handler.
/// Boxing and type-name dispatch are fine here — this shape must never ship in the seam itself,
/// where avoiding exactly that cost is the point.
/// </summary>
file sealed class TestObserver(Action<object> handle) : IProcessorObserver {
    public void OnStateChanged(in ProcessorStateChanged evt)       => handle(evt);
    public void OnRecordSkipped(in RecordSkipped evt)              => handle(evt);
    public void OnRecordReady(in RecordReady evt)                  => handle(evt);
    public void OnRecordCompleted(in RecordCompleted evt)          => handle(evt);
    public void OnBatchReady(in BatchReady evt)                    => handle(evt);
    public void OnBatchCompleted(in BatchCompleted evt)            => handle(evt);
    public void OnCheckpointCommitted(in CheckpointCommitted evt)  => handle(evt);
    public void OnCheckpointFailed(in CheckpointFailed evt)        => handle(evt);

    public static TestObserver For<TEvent>(Action<TEvent> handler) where TEvent : struct =>
        new(evt => { if (evt is TEvent typed) handler(typed); });
}
