using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Processors.Gateway.Messages;
using Kurrent.Surge.Processors.Models;
using Kurrent.Testkit.TUnit.Assertions;

namespace Kurrent.Surge.Streaming.Tests;

// A stream route resolves links: a row read through $ce-<category> is delivered at the link's position
// but carries its target's Position, which sits earlier in the log. The runtime must checkpoint, prune,
// and track by the position the subscription delivered it at, or a resume through the pointer stream
// lands behind the committed floor.
[Category("Processors")]
[Category("Integration")]
[Timeout(30_000)]
public class LinkResolvedCheckpointTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask resume_delivers_links_to_a_committed_record_and_tracks_them_at_the_link_position(CancellationToken ct) {
        // Arrange — a stream in a category of its own, so $ce-<category> holds nothing but its two
        // links. The first run tracks the first resolved row and stops, so the floor is that link's key.
        var category = $"linkresolved{Guid.NewGuid():N}";
        var stream   = StreamName.From($"{category}-{Guid.NewGuid():N}");
        var data     = await Fixture.TestData.GenerateSet(2, ct);

        await Fixture.Client.Streams.AppendStream(stream, data.Records, ct);

        var categoryStream = StreamName.From($"$ce-{category}");
        var expectedLinks  = data.Records.Length;

        // The $by_category projection is eventually consistent: the pointer stream must exist and
        // hold both links before a processor subscribes to it.
        await Assert.That(() => CountRows(categoryStream, ct))
            .EventuallyPasses(count => count.IsEqualTo(expectedLinks), options => {
                options.Timeout           = TimeSpan.FromSeconds(20);
                options.CancellationToken = ct;
                options.RetryOn<StreamNotFoundException>();
            });

        var processorId = $"link-resolved-{TestId.New()}";

        var first = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Stream == stream && !ctx.Record.Link.IsUnset) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        await KurrentProcessor
            .Create(first, opts => {
                Configure(opts, processorId, categoryStream);
                opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            })
            .RunAsync(ct);

        var firstFloor = first.Records.Single(r => r.Stream == stream && !r.Link.IsUnset).CheckpointPosition.LogPosition;

        var linkPosition = LogPosition.Unset;

        var second = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Stream == stream && !ctx.Record.Link.IsUnset) {
                    linkPosition = ctx.Record.CheckpointPosition.LogPosition;
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var committed = new CommittedPositionsObserver();

        var resume = KurrentProcessor.Create(second, opts => {
            Configure(opts, processorId, categoryStream);
            opts.AddObserver(committed);
        });

        // Act — resume from the committed floor. The second link sits past it and resolves to the
        // second record; a run that prunes or rejects it reaches the live edge instead, which ends the run.
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
        var linkDelivered     = second.Records.Any(r => r.Stream == stream && !r.Link.IsUnset);

        // Assert — the second link reached the handler, and its own key in $ce-<category>, past the
        // first run's floor, is what committed.
        await Assert.That(linkDelivered).IsTrue();
        await Assert.That(expectedCommitted).IsGreaterThan(firstFloor);
        await Assert.That(committed.Highest).IsEqualTo(expectedCommitted);
    }

    async Task<int> CountRows(StreamName stream, CancellationToken ct) {
        var count = 0;

        await foreach (var msg in Fixture.Client.Streams.ReadStream(stream, new ReadStreamOptions { SkipDecoding = true }, ct)) {
            if (msg.Value is Record)
                count++;
        }

        return count;
    }

    void Configure(ProcessorOptions opts, string processorId, StreamName categoryStream) {
        opts.Connection(Fixture.ConnectionString, client => client.LoggerFactory = Fixture.LoggerFactory);

        opts.ProcessorId         = processorId;
        opts.Subscription.Filter = RecordFilter.ByStream(categoryStream);
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
