// using Kurrent.Client.Exceptions;
// using Kurrent.Client.Streams;
// using Kurrent.Client.Tests.Filters;
//
// namespace Kurrent.Client.Tests.Modules.Streams;
//
// [Category("Streams")]
// [Category("RecordProcessor")]
// [Category("Exceptions")]
// public class ProcessorExceptionTests {
//     readonly RecordFaker _recordFaker = new();
//
//     [Test]
//     public async ValueTask initialize_exception_wraps_user_exception() {
//         // Arrange
//         var userException = new InvalidOperationException("Initialize failed");
//         var processor     = new ThrowingProcessor { ThrowOnInitialize = userException };
//         var messages      = CreateMessages(_recordFaker.Generate());
//
//         // Act
//         await messages.ProcessRecords("test-1", processor, CreateOptions(), TimeProvider.System);
//
//         // Assert
//         processor.ShutdownContext.ShouldNotBeNull();
//         processor.ShutdownContext.Reason.ShouldBe(ShutdownReason.Error);
//         processor.ShutdownContext.Error.ShouldNotBeNull();
//
//         var exception = (Exception)processor.ShutdownContext.Error!.Value;
//
//         exception.ShouldBeOfType<InitializeException>();
//         exception.InnerException.ShouldBe(userException);
//     }
//
//     [Test]
//     public async ValueTask process_records_exception_wraps_user_exception() {
//         // Arrange
//         var userException = new InvalidOperationException("ProcessRecords failed");
//         var processor     = new ThrowingProcessor { ThrowOnProcessRecords = userException };
//         var messages      = CreateMessages(_recordFaker.Generate());
//
//         // Act
//         await messages.ProcessRecords("test-2", processor, CreateOptions(), TimeProvider.System);
//
//         // Assert
//         processor.ShutdownContext.ShouldNotBeNull();
//         processor.ShutdownContext.Reason.ShouldBe(ShutdownReason.Error);
//         processor.ShutdownContext.Error.ShouldNotBeNull();
//
//         var exception = (Exception)processor.ShutdownContext.Error!.Value;
//
//         exception.ShouldBeOfType<ProcessRecordsException>();
//         exception.InnerException.ShouldBe(userException);
//     }
//
//     [Test]
//     public async ValueTask heartbeat_exception_wraps_user_exception() {
//         // Arrange
//         var userException = new InvalidOperationException("Heartbeat failed");
//         var processor     = new ThrowingProcessor { ThrowOnHeartbeat = userException };
//         var heartbeat     = Heartbeat.CreateCheckpoint(LogPosition.From(100), DateTimeOffset.UtcNow);
//         var messages      = CreateMessages(heartbeat);
//
//         // Act
//         await messages.ProcessRecords("test-3", processor, CreateOptions(), TimeProvider.System);
//
//         // Assert
//         processor.ShutdownContext.ShouldNotBeNull();
//         processor.ShutdownContext.Reason.ShouldBe(ShutdownReason.Error);
//         processor.ShutdownContext.Error.ShouldNotBeNull();
//
//         var exception = (Exception)processor.ShutdownContext.Error!.Value;
//
//         exception.ShouldBeOfType<HeartbeatException>();
//         exception.InnerException.ShouldBe(userException);
//     }
//
//     [Test]
//     public async ValueTask inner_exception_is_preserved() {
//         // Arrange
//         var userException = new InvalidOperationException("User code failed");
//         var processor     = new ThrowingProcessor { ThrowOnProcessRecords = userException };
//         var messages      = CreateMessages(_recordFaker.Generate());
//
//         // Act
//         await messages.ProcessRecords("test-4", processor, CreateOptions(), TimeProvider.System);
//
//         // Assert
//         var exception = (Exception)processor.ShutdownContext!.Error!.Value as ProcessRecordsException;
//
//         exception.ShouldNotBeNull();
//         exception.InnerException.ShouldNotBeNull();
//         exception.InnerException.ShouldBe(userException);
//         exception.InnerException!.Message.ShouldBe("User code failed");
//     }
//
//     [Test]
//     public async ValueTask operation_cancelled_exception_without_cancelled_token_results_in_error_not_wrapped() {
//         // Arrange - exception without a cancelled token
//         var processor = new ThrowingProcessor {
//             ThrowOnProcessRecords = new OperationCanceledException("User cancelled without token")
//         };
//
//         var messages = CreateMessages(_recordFaker.Generate());
//
//         // Act
//         await messages.ProcessRecords("test-5", processor, CreateOptions(), TimeProvider.System);
//
//         // Assert - OperationCanceledException without cancelled token should result in Error
//         processor.ShutdownContext.ShouldNotBeNull();
//         processor.ShutdownContext.Reason.ShouldBe(ShutdownReason.Error);
//
//         // The error should NOT be wrapped in a typed exception
//         var exception = (Exception)processor.ShutdownContext.Error!.Value;
//         exception.ShouldNotBeOfType<ProcessRecordsException>();
//         exception.ShouldNotBeOfType<InitializeException>();
//         exception.ShouldNotBeOfType<HeartbeatException>();
//     }
//
//     [Test]
//     public void exceptions_inherit_from_kurrent_exception() {
//         typeof(InitializeException).IsAssignableTo(typeof(KurrentException)).ShouldBeTrue();
//         typeof(ProcessRecordsException).IsAssignableTo(typeof(KurrentException)).ShouldBeTrue();
//         typeof(HeartbeatException).IsAssignableTo(typeof(KurrentException)).ShouldBeTrue();
//     }
//
//     #region . helpers .
//
//     static IAsyncEnumerable<ReadRecordsResult> CreateMessages(params object[] items) {
//         return items.Select<object, ReadRecordsResult>(item => item switch {
//             Record r    => r,     // implicit conversion
//             Heartbeat h => h,     // implicit conversion
//             _           => throw new ArgumentException($"Unsupported type: {item.GetType()}")
//         }).ToAsyncEnumerable();
//     }
//
//     static ReadStreamOptions CreateOptions() =>
//         new() { Stream = "test-stream", SkipDecoding = true };
//
//     class ThrowingProcessor : IRecordProcessor {
//         public Exception? ThrowOnInitialize     { get; init; }
//         public Exception? ThrowOnProcessRecords { get; init; }
//         public Exception? ThrowOnHeartbeat      { get; init; }
//
//         public ShutdownContext? ShutdownContext { get; private set; }
//
//         public ValueTask Initialize(InitializationContext context) {
//             if (ThrowOnInitialize is not null)
//                 throw ThrowOnInitialize;
//
//             return ValueTask.CompletedTask;
//         }
//
//         public ValueTask ProcessRecords(ProcessRecordsContext context) {
//             if (ThrowOnProcessRecords is not null)
//                 throw ThrowOnProcessRecords;
//
//             return ValueTask.CompletedTask;
//         }
//
//         public ValueTask OnHeartbeat(HeartbeatContext context) {
//             if (ThrowOnHeartbeat is not null)
//                 throw ThrowOnHeartbeat;
//
//             return ValueTask.CompletedTask;
//         }
//
//         public ValueTask OnShutdown(ShutdownContext context) {
//             ShutdownContext = context;
//             return ValueTask.CompletedTask;
//         }
//     }
//
//     #endregion
// }
