// using Kurrent.Client.Streams;
// using Kurrent.Client.Tests.Fixtures;
// using Serilog;
//
// namespace Kurrent.Client.Tests.Streams;
//
// [Category("Streams")]
// [Category("Read")]
// [Category("RecordProcessor")]
// [Category("Integration")]
// public class RecordProcessorTests {
//     [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
//     public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }
//
//     [Test]
//     public async ValueTask processes_all_stream_records(CancellationToken ct) {
//         var simulation = await Fixture.SeedSmartHomeActivity(10, ct);
//
//         var processor = new TestRecordProcessor();
//
//         await Fixture.Client.Streams.Read(
//             new ReadStreamOptions { Stream = simulation.Stream, SkipDecoding = true },
//             processor,
//             ct
//         );
//
//         processor.ProcessedRecords.Count.ShouldBe(simulation.Records.Count);
//         processor.InitializeCalled.ShouldBeTrue();
//         processor.ShutdownCalled.ShouldBeTrue();
//         processor.ShutdownContext!.Reason.ShouldBe(ShutdownReason.Completed);
//     }
//
//     [Test]
//     public async ValueTask processes_log_records_with_filter(CancellationToken ct) {
//         var simulation = await Fixture.SeedSmartHomeActivity(10, ct);
//
//         var processor = new TestRecordProcessor();
//
//         var options = new ReadOptions {
//             Start        = LogPosition.Earliest,
//             Filter       = RecordFilter.ByExpression($"stream = '{simulation.Stream}'"),
//             SkipDecoding = true
//         };
//
//         using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);
//
//         // cancel when we reach the expected position
//         processor.OnRecordProcessed = record => {
//             if (record.Position >= simulation.Position)
//                 cancellator.Cancel();
//         };
//
//         await Fixture.Client.Streams.Read(options, processor, cancellator.Token);
//
//         processor.ProcessedRecords.Count.ShouldBe(simulation.Records.Count);
//         processor.ShutdownCalled.ShouldBeTrue();
//     }
//
//     [Test]
//     public async ValueTask calls_on_initialize_with_correct_context(CancellationToken ct) {
//         var simulation = await Fixture.SeedSmartHomeActivity(5, ct);
//
//         var processor = new TestRecordProcessor();
//
//         var options = new ReadStreamOptions { Stream = simulation.Stream, SkipDecoding = true };
//
//         await Fixture.Client.Streams.Read(options, processor, ct);
//
//         processor.InitializeCalled.ShouldBeTrue();
//         processor.InitializeContext.ShouldNotBeNull();
//         processor.InitializeContext!.ProcessId.ShouldNotBeNullOrEmpty();
//         processor.InitializeContext.Options.ShouldBe(options);
//     }
//
//     [Test]
//     public async ValueTask calls_on_shutdown_with_statistics(CancellationToken ct) {
//         var simulation = await Fixture.SeedSmartHomeActivity(7, ct);
//
//         var processor = new TestRecordProcessor();
//
//         await Fixture.Client.Streams.Read(
//             new ReadStreamOptions { Stream = simulation.Stream, SkipDecoding = true },
//             processor,
//             ct
//         );
//
//         processor.ShutdownCalled.ShouldBeTrue();
//         processor.ShutdownContext.ShouldNotBeNull();
//         processor.ShutdownContext!.Reason.ShouldBe(ShutdownReason.Completed);
//         processor.ShutdownContext.TotalRecords.ShouldBe(simulation.Records.Count);
//         processor.ShutdownContext.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
//         processor.ShutdownContext.FinalPosition.ShouldNotBe(LogPosition.Unset);
//     }
//
//     [Test]
//     public async ValueTask calls_on_heartbeat_when_heartbeats_enabled(CancellationToken ct) {
//         var simulation = await Fixture.SeedSmartHomeActivity(10, ct);
//
//         var processor = new TestRecordProcessor();
//
//         using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);
//
//         var options = new ReadOptions {
//             Start        = LogPosition.Earliest,
//             Filter       = RecordFilter.ByExpression($"stream = '{simulation.Stream}'"),
//             SkipDecoding = true,
//             Heartbeat    = HeartbeatOptions.Threshold(5)
//         };
//
//         processor.OnRecordProcessed = record => {
//             if (record.Position >= simulation.Position)
//                 cancellator.Cancel();
//         };
//
//         await Fixture.Client.Streams.Read(options, processor, cancellator.Token);
//
//         // heartbeats may or may not be received depending on timing
//         // but if we got any, they should have valid positions
//         foreach (var heartbeat in processor.ReceivedHeartbeats)
//             heartbeat.Position.ShouldNotBe(LogPosition.Unset);
//     }
//
//     [Test]
//     public async ValueTask treats_cancellation_as_cancelled_shutdown(CancellationToken ct) {
//         var simulation = await Fixture.SeedSmartHomeActivity(20, ct);
//
//         var processor = new TestRecordProcessor();
//
//         using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);
//
//         processor.OnRecordProcessed = _ => {
//             if (processor.ProcessedRecords.Count >= 5)
//                 cancellator.Cancel();
//         };
//
//         await Fixture.Client.Streams.Read(
//             new ReadStreamOptions { Stream = simulation.Stream, SkipDecoding = true },
//             processor,
//             cancellator.Token
//         );
//
//         processor.ShutdownCalled.ShouldBeTrue();
//         processor.ShutdownContext!.Reason.ShouldBe(ShutdownReason.Cancelled);
//         processor.ProcessedRecords.Count.ShouldBeGreaterThanOrEqualTo(5);
//     }
//
//     [Test]
//     public async ValueTask uses_factory_to_create_processor(CancellationToken ct) {
//         var simulation = await Fixture.SeedSmartHomeActivity(5, ct);
//
//         var factory = new TestRecordProcessorFactory();
//
//         await Fixture.Client.Streams.Read(
//             new ReadStreamOptions { Stream = simulation.Stream, SkipDecoding = true },
//             factory,
//             ct
//         );
//
//         factory.CreateCount.ShouldBe(1);
//         factory.LastCreatedProcessor.ShouldNotBeNull();
//         factory.LastCreatedProcessor!.ProcessedRecords.Count.ShouldBe(simulation.Records.Count);
//     }
//
//     [Test]
//     public async ValueTask process_id_is_unique_per_instance(CancellationToken ct) {
//         var simulation = await Fixture.SeedSmartHomeActivity(3, ct);
//
//         var options = new ReadStreamOptions { Stream = simulation.Stream, SkipDecoding = true };
//
//         var processor1 = new TestRecordProcessor();
//         var processor2 = new TestRecordProcessor();
//
//         await Fixture.Client.Streams.Read(options, processor1, ct);
//         await Fixture.Client.Streams.Read(options, processor2, ct);
//
//         processor1.InitializeContext!.ProcessId.ShouldNotBeNullOrEmpty();
//         processor2.InitializeContext!.ProcessId.ShouldNotBeNullOrEmpty();
//         processor1.InitializeContext.ProcessId.ShouldNotBe(processor2.InitializeContext.ProcessId);
//     }
//
//     [Test]
//     [Arguments(1)]
//     [Arguments(5)]
//     [Arguments(10)]
//     [Arguments(25)]
//     public async ValueTask processes_correct_number_of_records(int recordCount, CancellationToken ct) {
//         var simulation = await Fixture.SeedSmartHomeActivity(recordCount, ct);
//
//         var processor = new TestRecordProcessor();
//
//         await Fixture.Client.Streams.Read(
//             new ReadStreamOptions { Stream = simulation.Stream, SkipDecoding = true },
//             processor,
//             ct
//         );
//
//         // use simulation.Records.Count as the seeder may add extra records
//         processor.ProcessedRecords.Count.ShouldBe(simulation.Records.Count);
//         processor.ShutdownContext!.TotalRecords.ShouldBe(simulation.Records.Count);
//     }
//
//     [Test]
//     public async ValueTask records_are_processed_in_order(CancellationToken ct) {
//         var simulation = await Fixture.SeedSmartHomeActivity(10, ct);
//
//         var processor = new TestRecordProcessor();
//
//         await Fixture.Client.Streams.Read(
//             new ReadStreamOptions { Stream = simulation.Stream, SkipDecoding = true },
//             processor,
//             ct
//         );
//
//         for (var i = 0; i < processor.ProcessedRecords.Count; i++) {
//             var record = processor.ProcessedRecords[i];
//             record.Revision.ShouldBe(Revision.From(i));
//         }
//     }
//
//     #region . test helpers .
//
//     class TestRecordProcessor : IRecordProcessor {
//         public List<Record>    ProcessedRecords   { get; } = [];
//         public List<Heartbeat> ReceivedHeartbeats { get; } = [];
//
//         public bool InitializeCalled { get; private set; }
//         public bool ShutdownCalled   { get; private set; }
//
//         public InitializationContext? InitializeContext { get; private set; }
//         public ShutdownContext?       ShutdownContext   { get; private set; }
//
//         public Action<Record>? OnRecordProcessed { get; set; }
//
//         public ValueTask Initialize(InitializationContext context) {
//             InitializeCalled  = true;
//             InitializeContext = context;
//
//             Log.Debug("Processor initialized: {ProcessId}", context.ProcessId);
//
//             return ValueTask.CompletedTask;
//         }
//
//         public ValueTask ProcessRecords(ProcessRecordsContext context) {
//             foreach (var record in context.Records) {
//                 ProcessedRecords.Add(record);
//
//                 Log.Debug(
//                     "Processed record {RecordId} @ {Position}",
//                     record.Id, record.Position
//                 );
//
//                 OnRecordProcessed?.Invoke(record);
//             }
//
//             return ValueTask.CompletedTask;
//         }
//
//         public ValueTask OnHeartbeat(HeartbeatContext context) {
//             ReceivedHeartbeats.Add(context.Heartbeat);
//
//             Log.Debug(
//                 "Heartbeat {Type} @ {Position}",
//                 context.Heartbeat.Type, context.Heartbeat.Position
//             );
//
//             return ValueTask.CompletedTask;
//         }
//
//         public ValueTask OnShutdown(ShutdownContext context) {
//             ShutdownCalled  = true;
//             ShutdownContext = context;
//
//             if (context.Reason == ShutdownReason.Error) {
//                 Log.Error(
//                     "Shutdown due to error: {Error} after {TotalRecords} records",
//                     context.Error, context.TotalRecords
//                 );
//             } else {
//                 Log.Debug(
//                     "Shutdown ({Reason}): {TotalRecords} records in {Duration}",
//                     context.Reason, context.TotalRecords, context.Duration
//                 );
//             }
//
//             return ValueTask.CompletedTask;
//         }
//     }
//
//     class TestRecordProcessorFactory : IRecordProcessorFactory {
//         public int                  CreateCount          { get; private set; }
//         public TestRecordProcessor? LastCreatedProcessor { get; private set; }
//
//         public IRecordProcessor CreateProcessor() {
//             CreateCount++;
//             return LastCreatedProcessor = new TestRecordProcessor();
//         }
//     }
//
//     #endregion
// }
