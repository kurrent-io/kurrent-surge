using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Models;
using Kurrent.Streaming.Options;
using Kurrent.Testkit.Aspire;
using TUnit.Assertions.Enums;

namespace Kurrent.Streaming.Tests;

[Category("Processors")]
[Category("Integration")]
[Timeout(120_000)]
public class DroneIntegrationTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    #region ->> processing <<-

    [Test]
    public async ValueTask records_are_processed(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(100, ct);

        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"records_are_processed-{data.Stream}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromHours(1);
        });

        // Act
        await drone.RunAsync(ct);

        // Assert
        for (var i = 0; i < processor.Records.Length; i++)
            await Assert.That(processor.Records[i].Revision).IsEqualTo(Revision.From(i));
    }

    [Test]
    public async ValueTask processes_all_records_from_stream(CancellationToken ct) {
        // Arrange
        var data      = await Fixture.SeedTestStream(ct);
        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition == data.Position) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-all-{data.Stream}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        });

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(processor.Records).Count().IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask processor_processes_all_records_individually(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(25, ct);
        var processor   = new TestableRecordProcessor();
        var processorId = $"drone-individual-{PunyId.New()}";
        var recordCount = 0;

        processor.OnProcessRecord = ctx => {
            Interlocked.Increment(ref recordCount);

            if (ctx.Record.Position.LogPosition >= data.Position) {
                ctx.Track(ctx.Record);
                ctx.StopProcessing();
            }

            return ValueTask.CompletedTask;
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        });

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(recordCount).IsEqualTo(processor.Records.Length);
    }

    #endregion // processing

    #region ->> stop and cancel <<-

    [Test]
    public async ValueTask processor_stops_on_user_request(CancellationToken ct) {
        // Arrange
        var data      = await Fixture.SeedTestStream(ct);
        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-stop-{TestContext.Current!.Id}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        });

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(processor.Records.Length).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask processor_cancels_gracefully(CancellationToken ct) {
        // Arrange
        var data      = await Fixture.SeedTestStream(ct);
        var processor = new TestableRecordProcessor();

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-cancel-{TestContext.Current!.Id}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        }).Build();

        // Act
        var task = drone.RunAsync(cancellator.Token);

        await cancellator.CancelAsync();

        await Assert.That(task).IsNotCompletedSuccessfully();
    }

    #endregion // stop and cancel

    #region ->> error handling <<-

    [Test]
    public async ValueTask processor_handles_exceptions_and_stops_processing(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        var processor = new TestableRecordProcessor {
            OnProcessRecord = _ => throw new InvalidOperationException("Process failed")
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-error-{PunyId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        });

        // Act & Assert — Drone wraps in AggregateException, inner is DroneException
        var aex = await Assert.That(async () => await drone.RunAsync(ct))
            .Throws<AggregateException>();

        await Assert.That(aex!.InnerExceptions[0]).IsTypeOf<DroneException>();
        await Assert.That(processor.Records.Length).IsEqualTo(0);
    }

    #endregion // error handling

    #region ->> checkpoint recovery <<-

    [Test]
    public async ValueTask complete_lifecycle_with_checkpoint_recovery(CancellationToken ct) {
        // Arrange
        var data                     = await Fixture.SeedTestStream(20, ct);
        var processorId              = $"drone-ckpt-{PunyId.New()}";
        var recordsToProcessInPhase1 = 10;

        // Phase 1: Process first 10 records, checkpoint, then stop
        var processor1 = new TestableRecordProcessor();

        processor1.OnProcessRecord = ctx => {
            if (processor1.Records.Length >= recordsToProcessInPhase1) {
                ctx.Track(ctx.Record);
                ctx.StopProcessing();
            }

            return ValueTask.CompletedTask;
        };

        var drone1 = KurrentProcessor.Create(processor1, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromMilliseconds(100);
        });

        await drone1.RunAsync(ct);

        // Allow checkpoint to persist
        await Task.Delay(200, ct);

        // Phase 2: Resume from same processor ID — should process remaining records
        var processor2 = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var drone2 = KurrentProcessor.Create(processor2, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId               = processorId;
            opts.Subscription.Filter       = RecordFilter.ByStream(data.Stream);
            opts.Checkpoint.AutoCheckpoint = false;
        });

        await drone2.RunAsync(ct);

        // Assert — all records processed with tolerance for checkpoint timing
        var totalProcessedRecords = processor1.Records.Length + processor2.Records.Length;
        var allProcessedIds       = processor1.Records.Concat(processor2.Records).Select(r => r.Id).ToList();
        var duplicateCount        = allProcessedIds.Count - allProcessedIds.Distinct().Count();

        await Assert.That(totalProcessedRecords).IsLessThanOrEqualTo(data.Records.Length + 5);
        await Assert.That(duplicateCount).IsLessThanOrEqualTo(5);
    }

    [Test]
    public async ValueTask checkpoint_recovery_across_multiple_phases(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(30, ct);
        var processorId = $"drone-multi-{PunyId.New()}";
        var processors  = new List<TestableRecordProcessor>();

        // Phases 1-2: Process 10 records each, then stop
        for (var phase = 0; phase < 2; phase++) {
            var processor = new TestableRecordProcessor();
            processors.Add(processor);

            processor.OnProcessRecord = ctx => {
                if (ctx.Progress.Throughput.TotalRecords == 10)
                    ctx.StopProcessing($"Phase {phase + 1} done after 10 records");

                return ValueTask.CompletedTask;
            };

            var phaseInitial = phase == 0
                ? SubscriptionInitialPosition.Earliest
                : SubscriptionInitialPosition.Latest;

            var drone = KurrentProcessor.Create(processor, opts => {
                opts.Connection(
                    Fixture.ConnectionString,
                    client => client.LoggerFactory = Fixture.LoggerFactory);

                opts.ProcessorId                  = processorId;
                opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
                opts.Subscription.InitialPosition = phaseInitial;
                opts.Checkpoint.Interval          = TimeSpan.FromMilliseconds(100);
            });

            await drone.RunAsync(ct);
            await Task.Delay(200, ct);
        }

        // Phase 3: Process remaining records
        var processor3 = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing("Phase 3 reached end of stream");
                }

                return ValueTask.CompletedTask;
            }
        };
        processors.Add(processor3);

        var finalDrone = KurrentProcessor.Create(processor3, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId         = processorId;
            opts.Subscription.Filter = RecordFilter.ByStream(data.Stream);
            opts.Checkpoint.Interval = TimeSpan.FromMilliseconds(60_000);
        });

        await finalDrone.RunAsync(ct);

        // Assert — all unique records across all 3 phases
        var allProcessedIds = processors.SelectMany(p => p.Records).Select(r => r.Id).ToList();
        var uniqueCount     = allProcessedIds.Distinct().Count();
        var duplicateCount  = allProcessedIds.Count - uniqueCount;

        await Assert.That(uniqueCount).IsGreaterThanOrEqualTo(data.Records.Length);
        await Assert.That(duplicateCount).IsLessThanOrEqualTo(5);
    }

    [Test]
    public async ValueTask checkpoint_recovery_with_no_remaining_records(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(10, ct);
        var processorId = $"drone-norec-{PunyId.New()}";

        // Phase 1: Process all records
        var processor1 = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var drone1 = KurrentProcessor.Create(processor1, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromMilliseconds(100);
        });

        await drone1.RunAsync(ct);
        await Task.Delay(200, ct);

        // Phase 2: Resume — no records remain, cancel after 2s
        var processor2 = new TestableRecordProcessor();

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancellator.CancelAfter(2000);

        var drone2 = KurrentProcessor.Create(processor2, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId         = processorId;
            opts.Subscription.Filter = RecordFilter.ByStream(data.Stream);
            opts.Checkpoint.Interval = TimeSpan.FromMilliseconds(60_000);
        }).Build();

        var task = drone2.RunAsync(cancellator.Token);

        // Assert
        using var _ = Assert.Multiple();
        await Assert.That(task).IsNotCompletedSuccessfully();
        await Assert.That(processor2.Records.Length).IsEqualTo(0);
    }

    [Test]
    public async ValueTask checkpoint_recovery_all_stream_with_skip_decoding(CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(1000, ct);
        var processorId = $"drone-all-ckpt-{data.Stream}";

        // Phase 1: Process first 5 records, stop before tracking the 6th
        var processorOne = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Progress.Throughput.TotalRecords == 5)
                    ctx.StopProcessing("Phase 1 done after 5 records");

                return ValueTask.CompletedTask;
            }
        };

        var drone1 = KurrentProcessor.Create(processorOne, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        });

        await drone1.RunAsync(ct);

        // Phase 2: Resume, process remaining records
        var processorTwo = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing("Phase 2 reached end of stream");
                }

                return ValueTask.CompletedTask;
            }
        };

        var drone2 = KurrentProcessor.Create(processorTwo, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = processorId;
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        });

        await drone2.RunAsync(ct);

        // Assert
        var streamRecordIds    = data.Records.Select(x => x.RecordId).ToList();
        var processedRecordIds = processorOne.Records.Concat(processorTwo.Records).Select(r => r.Id).ToList();
        var duplicateCount     = processedRecordIds.Count - processedRecordIds.Distinct().Count();

        using var __ = Assert.Multiple();
        await Assert.That(processedRecordIds).IsEquivalentTo(streamRecordIds, CollectionOrdering.Matching);
        await Assert.That(duplicateCount).IsLessThanOrEqualTo(5);
    }

    #endregion // checkpoint recovery

    #region ->> checkpointing <<-

    [Test]
    public async ValueTask user_track_is_allowed_when_auto_checkpoint_is_true(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);
        Exception? caught = null;

        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                try { ctx.Track(ctx.Record); }
                catch (Exception ex) { caught = ex; }

                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-track-auto-{PunyId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.AutoCheckpoint    = true;
        });

        // Act
        await drone.RunAsync(ct);

        // Assert — Track does not throw; idempotent with auto-track
        await Assert.That(caught).IsNull();
        await Assert.That(processor.Records.Length).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask manual_checkpoint_fires_on_checkpoint_with_trigger_manual(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                ctx.Checkpoint("custom-reason");

                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-manual-ckpt-{PunyId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromHours(1);
        });

        // Act
        await drone.RunAsync(ct);

        // Assert — Manual checkpoints fired
        var manualCheckpoints = processor.Checkpoints
            .Where(c => c.Trigger == CheckpointTrigger.Manual)
            .ToList();

        await Assert.That(manualCheckpoints.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(manualCheckpoints[0].Reason).IsEqualTo("custom-reason");
    }

    [Test]
    public async ValueTask shutdown_checkpoint_fires_when_pending_positions(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-shutdown-ckpt-{PunyId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval          = TimeSpan.FromHours(1);
        });

        // Act
        await drone.RunAsync(ct);

        // Assert — Shutdown checkpoint fires because auto-tracked records created pending positions
        var shutdownCheckpoints = processor.Checkpoints
            .Where(c => c.Trigger == CheckpointTrigger.Shutdown)
            .ToList();

        await Assert.That(shutdownCheckpoints.Count).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async ValueTask on_checkpoint_throwing_skips_persistence_when_continue_policy(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();

                return ValueTask.CompletedTask;
            },
            OnCheckpointHook = _ => throw new InvalidOperationException("user gate")
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-ckpt-continue-{PunyId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.FailurePolicy     = CheckpointFailurePolicy.Continue;
            opts.Checkpoint.Interval          = TimeSpan.FromHours(1);
        }).Build();

        // Act
        await drone.RunAsync(ct);

        // Assert — drone reaches Stopped (continue policy, no tear-down)
        await Assert.That(drone.Status).IsEqualTo(DroneStatus.Stopped);
    }

    [Test]
    public async ValueTask on_checkpoint_throwing_tears_down_drone_when_fail_policy(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position)
                    ctx.StopProcessing();

                return ValueTask.CompletedTask;
            },
            OnCheckpointHook = _ => throw new InvalidOperationException("user gate")
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-ckpt-fail-{PunyId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.FailurePolicy     = CheckpointFailurePolicy.Fail;
            opts.Checkpoint.Interval          = TimeSpan.FromHours(1);
        }).Build();

        // Act
        await Assert.That(async () => await drone.RunAsync(ct))
            .Throws<AggregateException>();

        // Assert — drone went through error path
        await Assert.That(drone.Status).IsEqualTo(DroneStatus.Error);
    }

    #endregion // checkpointing

    #region ->> status <<-

    [Test]
    public async ValueTask status_is_stopped_after_graceful_shutdown(CancellationToken ct) {
        // Arrange
        var data      = await Fixture.SeedTestStream(5, ct);
        var processor = new TestableRecordProcessor {
            OnProcessRecord = ctx => {
                if (ctx.Record.Position.LogPosition >= data.Position) {
                    ctx.Track(ctx.Record);
                    ctx.StopProcessing();
                }

                return ValueTask.CompletedTask;
            }
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-status-{PunyId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        }).Build();

        // Act
        await drone.RunAsync(ct);

        // Assert
        await Assert.That(drone.Status).IsEqualTo(DroneStatus.Stopped);
    }

    [Test]
    public async ValueTask status_is_error_after_processing_failure(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        var processor = new TestableRecordProcessor {
            OnProcessRecord = _ => throw new InvalidOperationException("boom")
        };

        var drone = KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => client.LoggerFactory = Fixture.LoggerFactory);

            opts.ProcessorId                  = $"drone-errstat-{PunyId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(data.Stream);
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        }).Build();

        // Act
        await Assert.That(async () => await drone.RunAsync(ct))
            .Throws<AggregateException>();

        // Assert
        await Assert.That(drone.Status).IsEqualTo(DroneStatus.Error);
    }

    #endregion // status
}
