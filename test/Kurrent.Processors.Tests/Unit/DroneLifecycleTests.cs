using Kurrent.Client;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Enums;
using TUnit.Mocks;
using Times = TUnit.Mocks.Times;

namespace Kurrent.Processors.Tests;

[Category("Processors")]
public class DroneLifecycleTests {
    static Drone CreateDrone(
        Mock<ISubscriptionSource>? source          = null,
        Mock<ICheckpointStore>?    checkpointStore = null,
        IRecordProcessor?          processor       = null,
        Mock<IKurrentClient>?      client          = null,
        bool                       ownsClient      = true,
        bool                       debug           = false
    ) {
        source          ??= EmptySource();
        checkpointStore ??= EmptyCheckpointStore();
        processor       ??= new NoOpProcessor();
        client          ??= Mock.Of<IKurrentClient>();

        var settings = new ProcessorSettings {
            ProcessorId = "test-processor",
            WorkerId    = "test-worker",
            Debug       = debug
        };

        return new Drone(
            settings,
            source.Object,
            checkpointStore.Object,
            processor,
            NullLoggerFactory.Instance,
            TimeProvider.System,
            client.Object,
            ownsClient
        );
    }

    static Mock<ISubscriptionSource> EmptySource() {
        var source = Mock.Of<ISubscriptionSource>();

        source.Subscribe(Any<IRecordFilter>(), Any<RecordPosition>(), Any<bool>(), Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<SubscribeMessage>());

        return source;
    }

    static Mock<ICheckpointStore> EmptyCheckpointStore() {
        var store = Mock.Of<ICheckpointStore>();

        store.LoadLatestAsync(Any<CancellationToken>())
            .Returns(ProcessorCheckpoint.Empty);

        return store;
    }


    static Mock<ISubscriptionSource> BlockingSource() {
        var source = Mock.Of<ISubscriptionSource>();

        source.Subscribe(Any<IRecordFilter>(), Any<RecordPosition>(), Any<bool>(), Any<CancellationToken>())
            .Returns((IRecordFilter _, RecordPosition _, bool _, CancellationToken ct)
                => new BlockingAsyncEnumerable<SubscribeMessage>(ct));

        return source;
    }

    static Mock<ISubscriptionSource> FailingSource(Exception ex) {
        var source = Mock.Of<ISubscriptionSource>();

        source.Subscribe(Any<IRecordFilter>(), Any<RecordPosition>(), Any<bool>(), Any<CancellationToken>())
            .Throws(ex);

        return source;
    }

    #region . cleanup .

    [Test]
    public async ValueTask cleanup_runs_on_normal_completion() {
        // Arrange
        var source = EmptySource();
        var client = Mock.Of<IKurrentClient>();
        var drone  = CreateDrone(source: source, ownsClient: true, client: client);

        // Act
        await drone.RunAsync(CancellationToken.None);

        // Assert
        source.DisposeAsync().WasCalled(Times.Once);
        client.DisposeAsync().WasCalled(Times.Once);
    }

    [Test]
    [Timeout(15_000)]
    public async ValueTask cleanup_runs_on_cancellation(CancellationToken ct) {
        // Arrange
        var cts    = new CancellationTokenSource();
        var source = BlockingSource();
        var client = Mock.Of<IKurrentClient>();
        var drone  = CreateDrone(source: source, ownsClient: true, client: client);

        // Act — pre-cancelled token triggers graceful HandleStopped (no throw)
        await cts.CancelAsync();
        await drone.RunAsync(cts.Token);

        // Assert
        source.DisposeAsync().WasCalled(Times.Once);
        client.DisposeAsync().WasCalled(Times.Once);
    }

    [Test]
    public async ValueTask cleanup_runs_on_processing_error() {
        // Arrange
        var source = FailingSource(new InvalidOperationException("connection failed"));
        var client = Mock.Of<IKurrentClient>();
        var drone  = CreateDrone(source: source, ownsClient: true, client: client);

        // Act
        await Assert.That(async () => await drone.RunAsync(CancellationToken.None))
            .Throws<AggregateException>();

        // Assert
        source.DisposeAsync().WasCalled(Times.Once);
        client.DisposeAsync().WasCalled(Times.Once);
    }

    [Test]
    public async ValueTask cleanup_disposes_all_resources_even_when_source_disposal_throws() {
        // Arrange
        var source = EmptySource();
        var client = Mock.Of<IKurrentClient>();

        source.DisposeAsync().Throws(new ObjectDisposedException("source"));

        var drone = CreateDrone(source: source, ownsClient: true, client: client);

        // Act — graceful shutdown absorbs disposal errors
        await drone.RunAsync(CancellationToken.None);

        // Assert — client was still disposed despite source throwing
        client.DisposeAsync().WasCalled(Times.Once);
    }

    [Test]
    public async ValueTask cleanup_is_idempotent() {
        // Arrange
        var source = EmptySource();
        var client = Mock.Of<IKurrentClient>();
        var drone  = CreateDrone(source: source, ownsClient: true, client: client);

        // Act — first call triggers cleanup via RunAsync
        await drone.RunAsync(CancellationToken.None);

        // Assert — second call throws started guard, not double-dispose
        await Assert.That(async () => await drone.RunAsync(CancellationToken.None))
            .Throws<InvalidOperationException>();

        source.DisposeAsync().WasCalled(Times.Once);
        client.DisposeAsync().WasCalled(Times.Once);
    }

    #endregion

    #region . run_async guards .

    [Test]
    public async ValueTask run_async_throws_if_called_twice() {
        // Arrange
        var drone = CreateDrone();
        await drone.RunAsync(CancellationToken.None);

        // Act & Assert
        await Assert.That(async () => await drone.RunAsync(CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    #endregion

    #region . client ownership .

    [Test]
    public async ValueTask client_not_disposed_when_not_owned() {
        // Arrange
        var client = Mock.Of<IKurrentClient>();
        var drone  = CreateDrone(ownsClient: false, client: client);

        // Act
        await drone.RunAsync(CancellationToken.None);

        // Assert
        client.DisposeAsync().WasNeverCalled();
    }

    [Test]
    public async ValueTask client_disposed_when_owned() {
        // Arrange
        var client = Mock.Of<IKurrentClient>();
        var drone  = CreateDrone(ownsClient: true, client: client);

        // Act
        await drone.RunAsync(CancellationToken.None);

        // Assert
        client.DisposeAsync().WasCalled(Times.Once);
    }

    #endregion

    #region . shutdown ordering .

    [Test]
    public async ValueTask shutdown_notifies_processor_before_cleanup() {
        // Arrange
        var callOrder = new List<string>();
        var source    = EmptySource();
        var processor = Mock.Of<IRecordProcessor>();

        processor.OnComplete(Any<CompleteContext>())
            .Callback(() => callOrder.Add("OnComplete"));

        source.DisposeAsync()
            .Callback(() => callOrder.Add("SourceDisposed"));

        var drone = CreateDrone(source: source, processor: processor.Object);

        // Act
        await drone.RunAsync(CancellationToken.None);

        // Assert — OnComplete called before source disposed (order matters)
        await Assert.That(callOrder).IsEquivalentTo(
            new[] { "OnComplete", "SourceDisposed" },
            CollectionOrdering.Matching);
    }

    #endregion

    #region . status transitions .

    [Test]
    public async ValueTask status_is_stopped_on_clean_shutdown() {
        // Arrange
        var drone = CreateDrone();

        // Act
        await drone.RunAsync(CancellationToken.None);

        // Assert
        await Assert.That(drone.Status).IsEqualTo(ProcessorWorkerStatus.Stopped);
    }

    [Test]
    public async ValueTask status_is_error_on_processing_failure() {
        // Arrange
        var source = FailingSource(new InvalidOperationException("boom"));
        var drone  = CreateDrone(source: source);

        // Act — capture exception so RunAsync doesn't escape
        await Assert.That(async () => await drone.RunAsync(CancellationToken.None))
            .Throws<AggregateException>();

        // Assert
        await Assert.That(drone.Status).IsEqualTo(ProcessorWorkerStatus.Error);
    }

    [Test]
    public async ValueTask status_is_error_when_source_disposal_fails_on_graceful_shutdown() {
        // Arrange
        var source = EmptySource();

        source.DisposeAsync().Throws(new ObjectDisposedException("source"));

        var drone = CreateDrone(source: source);

        // Act
        await drone.RunAsync(CancellationToken.None);

        // Assert
        await Assert.That(drone.Status).IsEqualTo(ProcessorWorkerStatus.Error);
    }

    #endregion

    #region . checkpoint on shutdown .

    [Test]
    public async ValueTask graceful_shutdown_skips_checkpoint_if_never_processing() {
        // Arrange — empty source means we never reach Processing status
        var store = EmptyCheckpointStore();
        var drone = CreateDrone(checkpointStore: store);

        // Act
        await drone.RunAsync(CancellationToken.None);

        // Assert — no checkpoint stored since we never processed anything
        store.StoreAsync(Any<string>(), Any<string>(), Any<PartitionPosition[]>(), Any<CancellationToken>())
            .WasNeverCalled();
    }

    [Test]
    public async ValueTask checkpoint_store_is_loaded_on_startup() {
        // Arrange
        var store = EmptyCheckpointStore();
        var drone = CreateDrone(checkpointStore: store);

        // Act
        await drone.RunAsync(CancellationToken.None);

        // Assert
        store.LoadLatestAsync(Any<CancellationToken>()).WasCalled(Times.Once);
    }

    #endregion

    #region . error shutdown .

    [Test]
    public async ValueTask error_shutdown_always_throws_aggregate_with_original_error() {
        // Arrange
        var originalError = new InvalidOperationException("original");
        var source        = FailingSource(originalError);
        var drone         = CreateDrone(source: source);

        // Act
        var aex = await Assert.That(async () => await drone.RunAsync(CancellationToken.None))
            .Throws<AggregateException>();

        // Assert — original error is the first inner exception
        await Assert.That(aex!.InnerExceptions).IsNotEmpty();
        await Assert.That(aex.InnerExceptions[0]).IsTypeOf<InvalidOperationException>();
        await Assert.That(aex.InnerExceptions[0].Message).IsEqualTo("original");
    }

    [Test]
    public async ValueTask error_shutdown_does_not_checkpoint() {
        // Arrange
        var store  = EmptyCheckpointStore();
        var source = FailingSource(new InvalidOperationException("boom"));
        var drone  = CreateDrone(source: source, checkpointStore: store);

        // Act
        await Assert.That(async () => await drone.RunAsync(CancellationToken.None))
            .Throws<AggregateException>();

        // Assert
        store.StoreAsync(Any<string>(), Any<string>(), Any<PartitionPosition[]>(), Any<CancellationToken>())
            .WasNeverCalled();
    }

    [Test]
    public async ValueTask error_shutdown_calls_on_error_handler() {
        // Arrange
        var processor = Mock.Of<IRecordProcessor>();
        var source    = FailingSource(new InvalidOperationException("boom"));
        var drone     = CreateDrone(source: source, processor: processor.Object);

        // Act
        await Assert.That(async () => await drone.RunAsync(CancellationToken.None))
            .Throws<AggregateException>();

        // Assert
        processor.OnError(Any<ErrorContext>()).WasCalled(Times.Once);
    }

    [Test]
    public async ValueTask error_shutdown_accumulates_on_error_handler_failure() {
        // Arrange
        var processor = Mock.Of<IRecordProcessor>();
        var source    = FailingSource(new InvalidOperationException("original"));

        processor.OnError(Any<ErrorContext>())
            .Throws(new InvalidOperationException("handler bug"));

        var drone = CreateDrone(source: source, processor: processor.Object);

        // Act
        var aex = await Assert.That(async () => await drone.RunAsync(CancellationToken.None))
            .Throws<AggregateException>();

        // Assert — both errors in aggregate: original + handler failure
        await Assert.That(aex!.InnerExceptions).Count().IsGreaterThanOrEqualTo(2);
    }

    #endregion

    #region . debug mode .

    [Test]
    public async ValueTask debug_mode_propagates_on_complete_failure() {
        // Arrange
        var processor = Mock.Of<IRecordProcessor>();

        processor.OnComplete(Any<CompleteContext>())
            .Throws(new KurrentWorkerException("TEST", "handler bug", new InvalidOperationException("inner")));

        var drone = CreateDrone(processor: processor.Object, debug: true);

        // Act — in Debug mode, the user handler failure escalates to error shutdown
        var aex = await Assert.That(async () => await drone.RunAsync(CancellationToken.None))
            .Throws<AggregateException>();

        // Assert — the handler exception surfaces in the aggregate
        await Assert.That(aex!.InnerExceptions).IsNotEmpty();
    }

    [Test]
    public async ValueTask production_mode_swallows_on_complete_failure() {
        // Arrange
        var processor = Mock.Of<IRecordProcessor>();

        processor.OnComplete(Any<CompleteContext>())
            .Throws(new KurrentWorkerException("TEST", "handler bug", new InvalidOperationException("inner")));

        var drone = CreateDrone(processor: processor.Object, debug: false);

        // Act — in production mode, handler failure is logged and swallowed
        await drone.RunAsync(CancellationToken.None);

        // Assert — completed with Error status (handler failure accumulated, not thrown)
        await Assert.That(drone.Status).IsEqualTo(ProcessorWorkerStatus.Error);
    }

    #endregion
}
