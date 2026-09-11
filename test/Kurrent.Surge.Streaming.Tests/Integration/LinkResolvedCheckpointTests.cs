using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Processors.Gateway.Messages;

namespace Kurrent.Surge.Streaming.Tests;

// A resolved link is delivered at the link's position but carries its target's Position, which sits
// earlier in the log. The runtime must checkpoint, prune, and track by the position the subscription
// delivered it at. The standard projections write a $streams, $ce-*, and $et-* link after every record,
// so a raw-log processor that commits a record always meets links to it past the committed floor.
[Category("Processors")]
[Category("Integration")]
[Timeout(30_000)]
public class LinkResolvedCheckpointTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask resume_delivers_links_to_a_committed_record_and_tracks_them_at_the_link_position(CancellationToken ct) {
        // Arrange — a first run commits the record itself and stops, so the floor is the record's own
        // position. By then the projections have linked it from positions past that floor.
        var anchor      = await Fixture.LatestRecordPosition(ct);
        var target      = await Fixture.SeedTestStream(1, ct);
        var processorId = $"link-resolved-{TestId.New()}";

        var first = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Stream == target.Stream && ctx.Record.Link.IsUnset) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        await KurrentProcessor
            .Create(first, opts => {
                Configure(opts, processorId);
                opts.Subscription.StartPosition = anchor.LogPosition;
            })
            .RunAsync(ct);

        var linkPosition = LogPosition.Unset;

        var second = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Stream == target.Stream && !ctx.Record.Link.IsUnset) {
                    linkPosition = ctx.Record.CheckpointPosition.LogPosition;
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var committed = new CommittedPositionsObserver();

        var resume = KurrentProcessor.Create(second, opts => {
            Configure(opts, processorId);
            opts.AddObserver(committed);
        });

        // Act — resume from the committed floor. The links sit past it and resolve to the record at it;
        // a run that prunes or rejects them reaches the live edge instead, which ends the run.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var run = resume.RunAsync(stop.Token).AsTask();

        await Task.WhenAny(run, committed.CaughtUp);

        if (!run.IsCompleted)
            await stop.CancelAsync();

        try {
            await run;
        }
        catch (OperationCanceledException) { }

        var expectedCommitted = linkPosition;
        var linkDelivered     = second.Records.Any(r => r.Stream == target.Stream && !r.Link.IsUnset);

        // Assert — the link reached the handler and its own position, past the record's, is what committed.
        await Assert.That(linkDelivered).IsTrue();
        await Assert.That(expectedCommitted).IsGreaterThan(target.Position);
        await Assert.That(committed.Highest).IsEqualTo(expectedCommitted);
    }

    void Configure(ProcessorOptions opts, string processorId) {
        opts.Connection(Fixture.ConnectionString, client => client.LoggerFactory = Fixture.LoggerFactory);

        opts.ProcessorId         = processorId;
        opts.Subscription.Filter = RecordFilter.None;
        opts.Checkpoint.Interval = TimeSpan.FromHours(1);
    }
}

// The seam dispatches synchronously on the single processing thread; the test thread only reads after
// the run has ended or the CaughtUp signal fired.
file sealed class CommittedPositionsObserver : IProcessorObserver {
    readonly TaskCompletionSource _caughtUp = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LogPosition Highest  { get; private set; } = LogPosition.Unset;
    public Task        CaughtUp => _caughtUp.Task;

    public void OnCheckpointCommitted(in CheckpointCommitted evt) {
        foreach (var position in evt.Positions) {
            if (position.LogPosition > Highest)
                Highest = position.LogPosition;
        }

        if (evt.Trigger == CheckpointTrigger.CaughtUp)
            _caughtUp.TrySetResult();
    }
}
