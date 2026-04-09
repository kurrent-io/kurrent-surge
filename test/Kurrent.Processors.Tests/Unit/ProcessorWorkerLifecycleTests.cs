using Kurrent.Client;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Processors.Tests;

[Category("Processors")]
public class ProcessorWorkerLifecycleTests {
    static ProcessorWorker CreateWorker(
        IProcessorGateway? gateway   = null,
        IRecordProcessor?  processor = null,
        KurrentClient?     client    = null,
        bool               ownsClient = true
    ) {
        client    ??= A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));
        processor ??= new NoOpProcessor();
        gateway   ??= EmptyGateway();

        var settings = new ProcessorSettings {
            ProcessorId = "test-processor",
            WorkerId    = "test-worker"
        };

        return new ProcessorWorker(
            settings,
            client,
            gateway,
            processor,
            NullLoggerFactory.Instance,
            TimeProvider.System,
            ownsClient
        );
    }

    static IProcessorGateway EmptyGateway() {
        var gateway = A.Fake<IProcessorGateway>();

        A.CallTo(() => gateway.ConnectAsync(A<ProcessorSettings>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ProcessorControlMessage>());

        A.CallTo(() => gateway.SendAsync(A<WorkerMessage>._))
            .Returns(ValueTask.CompletedTask);

        return gateway;
    }

    #region . cleanup .

    [Test]
    public async Task cleanup_runs_on_normal_completion() {
        // Arrange
        var gateway = EmptyGateway();
        var client  = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));
        var worker  = CreateWorker(gateway: gateway, ownsClient: true, client: client);

        // Act
        await worker.RunAsync(CancellationToken.None);

        // Assert
        A.CallTo(() => gateway.DisposeAsync()).MustHaveHappenedOnceExactly();
        A.CallTo(() => client.DisposeAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task cleanup_runs_on_cancellation() {
        // Arrange
        var cts     = new CancellationTokenSource();
        var gateway = A.Fake<IProcessorGateway>();
        var client  = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));

        A.CallTo(() => gateway.ConnectAsync(A<ProcessorSettings>._, A<CancellationToken>._))
            .ReturnsLazily((ProcessorSettings _, CancellationToken ct) => new BlockingAsyncEnumerable<ProcessorControlMessage>(ct));

        A.CallTo(() => gateway.SendAsync(A<WorkerMessage>._))
            .Returns(ValueTask.CompletedTask);

        var worker = CreateWorker(gateway: gateway, ownsClient: true, client: client);

        // Act
        cts.Cancel();

        try {
            await worker.RunAsync(cts.Token);
        }
        catch (OperationCanceledException) {
            // expected
        }

        // Assert
        A.CallTo(() => gateway.DisposeAsync()).MustHaveHappenedOnceExactly();
        A.CallTo(() => client.DisposeAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task cleanup_runs_on_processing_error() {
        // Arrange
        var processor = A.Fake<IRecordProcessor>();
        var gateway   = A.Fake<IProcessorGateway>();
        var client    = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));

        A.CallTo(() => processor.OnError(A<ErrorContext>._)).Returns(ValueTask.CompletedTask);

        A.CallTo(() => gateway.ConnectAsync(A<ProcessorSettings>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("connection failed"));

        var worker = CreateWorker(gateway: gateway, processor: processor, ownsClient: true, client: client);

        // Act
        try {
            await worker.RunAsync(CancellationToken.None);
        }
        catch (InvalidOperationException) {
            // expected
        }

        // Assert
        A.CallTo(() => client.DisposeAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task cleanup_disposes_all_resources_even_when_gateway_disposal_throws() {
        // Arrange
        var gateway = EmptyGateway();
        var client  = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));

        A.CallTo(() => gateway.DisposeAsync())
            .ThrowsAsync(new ObjectDisposedException("gateway"));

        var worker = CreateWorker(gateway: gateway, ownsClient: true, client: client);

        // Act
        var ex = await Assert.That(async () => await worker.RunAsync(CancellationToken.None)).Throws<AggregateException>();

        // Assert — client was still disposed despite gateway throwing
        A.CallTo(() => client.DisposeAsync()).MustHaveHappenedOnceExactly();
        await Assert.That(ex!.InnerExceptions.Count).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task cleanup_throws_aggregate_when_multiple_disposals_fail() {
        // Arrange
        var gateway = EmptyGateway();
        var client  = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));

        A.CallTo(() => gateway.DisposeAsync())
            .ThrowsAsync(new ObjectDisposedException("gateway"));

        A.CallTo(() => client.DisposeAsync())
            .ThrowsAsync(new ObjectDisposedException("client"));

        var worker = CreateWorker(gateway: gateway, ownsClient: true, client: client);

        // Act
        var ex = await Assert.That(async () => await worker.RunAsync(CancellationToken.None)).Throws<AggregateException>();

        // Assert — both errors collected
        await Assert.That(ex!.InnerExceptions.Count).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task cleanup_is_idempotent() {
        // Arrange
        var gateway = EmptyGateway();
        var client  = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));
        var worker  = CreateWorker(gateway: gateway, ownsClient: true, client: client);

        // Act — first call triggers cleanup via RunAsync
        await worker.RunAsync(CancellationToken.None);

        // Second call throws InvalidOperationException (started guard), not double-dispose
        await Assert.That(async () => await worker.RunAsync(CancellationToken.None)).Throws<InvalidOperationException>();

        // Assert — resources were only disposed once
        A.CallTo(() => gateway.DisposeAsync()).MustHaveHappenedOnceExactly();
        A.CallTo(() => client.DisposeAsync()).MustHaveHappenedOnceExactly();
    }

    #endregion

    #region . run_async guards .

    [Test]
    public async Task run_async_throws_if_called_twice() {
        // Arrange
        var worker = CreateWorker();

        // Act
        await worker.RunAsync(CancellationToken.None);

        // Assert
        await Assert.That(async () => await worker.RunAsync(CancellationToken.None)).Throws<InvalidOperationException>();
    }

    #endregion

    #region . client ownership .

    [Test]
    public async Task client_not_disposed_when_not_owned() {
        // Arrange
        var client = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));
        var worker = CreateWorker(ownsClient: false, client: client);

        // Act
        await worker.RunAsync(CancellationToken.None);

        // Assert
        A.CallTo(() => client.DisposeAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task client_disposed_when_owned() {
        // Arrange
        var client = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));
        var worker = CreateWorker(ownsClient: true, client: client);

        // Act
        await worker.RunAsync(CancellationToken.None);

        // Assert
        A.CallTo(() => client.DisposeAsync()).MustHaveHappenedOnceExactly();
    }

    #endregion

    #region . shutdown notification ordering .

    [Test]
    public async Task shutdown_notifies_processor_before_cleanup() {
        // Arrange
        var callOrder = new List<string>();
        var gateway   = EmptyGateway();
        var processor = A.Fake<IRecordProcessor>();

        A.CallTo(() => processor.OnComplete(A<CompleteContext>._))
            .Invokes(() => callOrder.Add("OnComplete"))
            .Returns(ValueTask.CompletedTask);

        A.CallTo(() => gateway.DisposeAsync())
            .Invokes(() => callOrder.Add("GatewayDisposed"))
            .Returns(ValueTask.CompletedTask);

        var worker = CreateWorker(gateway: gateway, processor: processor);

        // Act
        await worker.RunAsync(CancellationToken.None);

        // Assert — OnComplete called before gateway disposed
        await Assert.That(callOrder).IsEquivalentTo(new[] { "OnComplete", "GatewayDisposed" });
    }

    #endregion
}

#region Test helpers

class NoOpProcessor : IRecordProcessor {
    public ValueTask ProcessRecord(ProcessRecordContext context) => ValueTask.CompletedTask;
}

class BlockingAsyncEnumerable<T>(CancellationToken externalCt = default) : IAsyncEnumerable<T> {
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default) {
        var linked = externalCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(externalCt, ct).Token
            : ct;

        return new BlockingEnumerator(linked);
    }

    class BlockingEnumerator(CancellationToken ct) : IAsyncEnumerator<T> {
        public T Current => default!;

        public async ValueTask<bool> MoveNextAsync() {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return false;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

#endregion
