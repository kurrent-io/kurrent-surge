using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Testing;
using Kurrent.Processors.Tests.Fakes;

namespace Kurrent.Processors.Tests;

[Category("Processors")]
public class MessageProcessorTests {
	readonly RecordFaker _recordFaker = new();

	#region . typed dispatch .

	[Test]
	public async ValueTask dispatches_to_registered_async_handler() {
		// Arrange
		var captured = (OrderPlaced?)null;

        IRecordProcessor processor = new MessageProcessor()
			.Process<OrderPlaced>((msg, _) => {
				captured = msg;
				return ValueTask.CompletedTask;
			});

		var message = new OrderPlaced("ORD-123", 99.99m);
		var context = CreateContext(message);

		// Act
		await processor.ProcessRecord(context);

		// Assert
		await Assert.That(captured).IsNotNull();
		await Assert.That(captured!.OrderId).IsEqualTo("ORD-123");
		await Assert.That(captured.Amount).IsEqualTo(99.99m);
	}

	[Test]
	public async ValueTask dispatches_to_registered_sync_handler() {
		// Arrange
		var captured = (OrderShipped?)null;

		var processor = new MessageProcessor()
			.Process<OrderShipped>((msg, _) => { captured = msg; });

		var message = new OrderShipped("ORD-456", "TRACK-789");
		var context = CreateContext(message);

		// Act
		await ((IRecordProcessor)processor).ProcessRecord(context);

		// Assert
		await Assert.That(captured).IsNotNull();
		await Assert.That(captured!.TrackingNumber).IsEqualTo("TRACK-789");
	}

	[Test]
	public async ValueTask dispatches_to_correct_handler_when_multiple_registered() {
		// Arrange
		var placedCount  = 0;
		var shippedCount = 0;

		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => { placedCount++; })
			.Process<OrderShipped>((_, _) => { shippedCount++; });

		// Act
		await ((IRecordProcessor)processor).ProcessRecord(CreateContext(new OrderPlaced("A", 1)));
		await ((IRecordProcessor)processor).ProcessRecord(CreateContext(new OrderShipped("B", "T")));
		await ((IRecordProcessor)processor).ProcessRecord(CreateContext(new OrderPlaced("C", 2)));

		// Assert
		await Assert.That(placedCount).IsEqualTo(2);
		await Assert.That(shippedCount).IsEqualTo(1);
	}

	[Test]
	public async ValueTask last_registration_wins_for_same_type() {
		// Arrange
		var firstCalled  = false;
		var secondCalled = false;

		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => { firstCalled = true; })
			.Process<OrderPlaced>((_, _) => { secondCalled = true; });

		// Act
		await ((IRecordProcessor)processor).ProcessRecord(CreateContext(new OrderPlaced("X", 1)));

		// Assert
		await Assert.That(firstCalled).IsFalse();
		await Assert.That(secondCalled).IsTrue();
	}

	#endregion

	#region . fallback .

	[Test]
	public async ValueTask invokes_async_fallback_for_unregistered_type() {
		// Arrange
		var fallbackCalled = false;

		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => { })
			.Fallback(_ => {
				fallbackCalled = true;
				return ValueTask.CompletedTask;
			});

		// Act — OrderShipped has no handler
		await ((IRecordProcessor)processor).ProcessRecord(CreateContext(new OrderShipped("A", "T")));

		// Assert
		await Assert.That(fallbackCalled).IsTrue();
	}

	[Test]
	public async ValueTask invokes_sync_fallback_for_unregistered_type() {
		// Arrange
		var fallbackCalled = false;

		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => { })
			.Fallback(_ => { fallbackCalled = true; });

		// Act
		await ((IRecordProcessor)processor).ProcessRecord(CreateContext(new OrderShipped("A", "T")));

		// Assert
		await Assert.That(fallbackCalled).IsTrue();
	}

	[Test]
	public async ValueTask does_not_invoke_fallback_when_handler_matches() {
		// Arrange
		var handlerCalled  = false;
		var fallbackCalled = false;

		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => { handlerCalled = true; })
			.Fallback(_ => { fallbackCalled = true; });

		// Act
		await ((IRecordProcessor)processor).ProcessRecord(CreateContext(new OrderPlaced("A", 1)));

		// Assert
		await Assert.That(handlerCalled).IsTrue();
		await Assert.That(fallbackCalled).IsFalse();
	}

	#endregion

	#region . undecoded records .

	[Test]
	public async ValueTask skips_dispatch_when_record_is_not_decoded() {
		// Arrange
		var handlerCalled = false;

		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => { handlerCalled = true; });

		var record  = _recordFaker.Generate(); // not decoded — Value is null
		var context = CreateContext(record);

		// Act
		await ((IRecordProcessor)processor).ProcessRecord(context);

		// Assert
		await Assert.That(handlerCalled).IsFalse();
	}

	[Test]
	public async ValueTask invokes_fallback_when_record_is_not_decoded() {
		// Arrange
		var fallbackCalled = false;

		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => { })
			.Fallback(_ => { fallbackCalled = true; });

		var record  = _recordFaker.Generate(); // not decoded
		var context = CreateContext(record);

		// Act
		await ((IRecordProcessor)processor).ProcessRecord(context);

		// Assert
		await Assert.That(fallbackCalled).IsTrue();
	}

	[Test]
	public async ValueTask completes_silently_when_no_handler_and_no_fallback() {
		// Arrange
		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => { });

		var context = CreateContext(new OrderShipped("A", "T")); // no handler for this type

		// Act & Assert — should not throw
		await ((IRecordProcessor)processor).ProcessRecord(context);
	}

	#endregion

	#region . can process .

	[Test]
	public async ValueTask can_process_returns_true_for_registered_decoded_type() {
		// Arrange
		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => { });

		var message = new OrderPlaced("A", 1);
		var record  = _recordFaker.Generate().WithDecodedValue(message);

		// Act & Assert
		await Assert.That(processor.CanProcess(record)).IsTrue();
	}

	[Test]
	public async ValueTask can_process_returns_false_for_unregistered_type() {
		// Arrange
		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => { });

		var record = _recordFaker.Generate().WithDecodedValue(new OrderShipped("A", "T"));

		// Act & Assert
		await Assert.That(processor.CanProcess(record)).IsFalse();
	}

	[Test]
	public async ValueTask can_process_returns_false_for_undecoded_record() {
		// Arrange
		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => { });

		var record = _recordFaker.Generate(); // not decoded

		// Act & Assert
		await Assert.That(processor.CanProcess(record)).IsFalse();
	}

	#endregion

	#region . context access .

	[Test]
	public async ValueTask handler_receives_full_context() {
		// Arrange
		ProcessRecordContext? captured = null;

		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, ctx) => { captured = ctx; });

		var message = new OrderPlaced("CTX-1", 50m);
		var context = CreateContext(message);

		// Act
		await ((IRecordProcessor)processor).ProcessRecord(context);

		// Assert
		await Assert.That(captured).IsNotNull();
		await Assert.That(captured!.Record).IsEqualTo(context.Record);
		await Assert.That(captured.ProcessorId).IsEqualTo(context.ProcessorId);
	}

	#endregion

	#region . subclass pattern .

	[Test]
	public async ValueTask subclass_registration_works() {
		// Arrange
		var processor = new TestOrderProcessor();
		var message   = new OrderPlaced("SUB-1", 77m);
		var context   = CreateContext(message);

		// Act
		await ((IRecordProcessor)processor).ProcessRecord(context);

		// Assert
		await Assert.That(processor.ProcessedOrders).Count().IsEqualTo(1);
		await Assert.That(processor.ProcessedOrders[0].OrderId).IsEqualTo("SUB-1");
	}

	#endregion

	#region . lifecycle hooks .

	[Test]
	public async ValueTask on_checkpoint_is_invoked() {
		// Arrange
		var checkpointCalled = false;

		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => { })
			.OnCheckpoint(_ => { checkpointCalled = true; return ValueTask.CompletedTask; });

		var ctx = new CheckpointContext {
			ProcessorId  = "test-processor",
			DroneId      = "test-drone",
			Positions    = [],
			CheckpointId = "cp-1",
			Trigger      = CheckpointTrigger.Interval,
			Timestamp    = DateTimeOffset.UtcNow
		};

		// Act
		await ((IRecordProcessor)processor).OnCheckpoint(ctx);

		// Assert
		await Assert.That(checkpointCalled).IsTrue();
	}

	[Test]
	public async ValueTask on_error_is_invoked() {
		// Arrange
		Exception? captured = null;

		var processor = new MessageProcessor()
			.OnError(ctx => { captured = ctx.Error; });

		var ctx = new ErrorContext {
			ProcessorId = "test-processor",
			DroneId     = "test-drone",
			Progress    = DroneProgress.Empty,
			Error       = new InvalidOperationException("boom")
		};

		// Act
		await ((IRecordProcessor)processor).OnError(ctx);

		// Assert
		await Assert.That(captured).IsNotNull();
		await Assert.That(captured!.Message).IsEqualTo("boom");
	}

	[Test]
	public async ValueTask on_complete_is_invoked() {
		// Arrange
		var completeCalled = false;

		var processor = new MessageProcessor()
			.OnComplete(_ => { completeCalled = true; });

		var ctx = new CompleteContext {
			ProcessorId = "test-processor",
			DroneId     = "test-drone",
			Progress    = DroneProgress.Empty
		};

		// Act
		await ((IRecordProcessor)processor).OnComplete(ctx);

		// Assert
		await Assert.That(completeCalled).IsTrue();
	}

	[Test]
	public async ValueTask lifecycle_hooks_are_noop_when_not_registered() {
		// Arrange
		var processor = (IRecordProcessor)new MessageProcessor();

		// Act & Assert — should not throw
		await processor.OnCheckpoint(new CheckpointContext {
			ProcessorId = "test", DroneId = "test",
			Positions = [], CheckpointId = "cp", Trigger = CheckpointTrigger.Interval, Timestamp = DateTimeOffset.UtcNow
		});
		await processor.OnError(new ErrorContext {
			ProcessorId = "test", DroneId = "test",
			Progress = DroneProgress.Empty, Error = new Exception()
		});
		await processor.OnComplete(new CompleteContext {
			ProcessorId = "test", DroneId = "test",
			Progress = DroneProgress.Empty
		});
	}

	#endregion

	#region . drone compatibility .

	[Test]
	public async ValueTask implements_irecord_processor() {
		// Arrange
		var processor = new MessageProcessor();

		// Act & Assert
		await Assert.That(processor).IsAssignableTo<IRecordProcessor>();
	}

	[Test]
	public async ValueTask works_with_safe_record_processor_wrapper() {
		// Arrange
		var captured = (OrderPlaced?)null;

		var inner = new MessageProcessor()
			.Process<OrderPlaced>((msg, _) => {
				captured = msg;
				return ValueTask.CompletedTask;
			});

		var safe    = new SafeRecordProcessor(inner);
		var message = new OrderPlaced("SAFE-1", 25m);
		var context = CreateContext(message);

		// Act
		await safe.ProcessRecord(context);

		// Assert
		await Assert.That(captured).IsNotNull();
		await Assert.That(captured!.OrderId).IsEqualTo("SAFE-1");
	}

	#endregion

	#region Test helpers

	ProcessRecordContext CreateContext<T>(T message) where T : notnull {
		var record = _recordFaker.Generate().WithDecodedValue(message);
		return CreateContext(record);
	}

	static ProcessRecordContext CreateContext(Record record) =>
		new() {
			ProcessorId = "test-processor",
			DroneId     = "test-drone",
			Record      = record,
			Progress    = DroneProgress.Empty
		};

	record OrderPlaced(string OrderId, decimal Amount);
	record OrderShipped(string OrderId, string TrackingNumber);

	class TestOrderProcessor : MessageProcessor {
		public List<OrderPlaced> ProcessedOrders { get; } = [];

		public TestOrderProcessor() {
			Process<OrderPlaced>((msg, _) => {
				ProcessedOrders.Add(msg);
			});
		}
	}

	#endregion
}
