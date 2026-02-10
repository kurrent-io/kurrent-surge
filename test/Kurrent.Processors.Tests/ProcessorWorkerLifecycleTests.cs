using Kurrent.Client;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Processors;
using Kurrent.Processors.Diagnostics;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Processors.Tests;

[Category("Processors")]
public class ProcessorWorkerLifecycleTests {
    static ProcessorWorker CreateWorker(
        IProcessorGateway gateway,
        IRecordProcessor? processor = null,
        bool ownsClient = true,
        KurrentClient? client = null
    ) {
        client ??= A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));
        processor ??= new NoOpProcessor();

        var settings = new ProcessorSettings {
            ProcessorId = "test-processor",
            WorkerId    = "test-worker"
        };

        var diagnostics = new WorkerTelemetry(
            NullLoggerFactory.Instance.CreateLogger(processor.GetType().FullName ?? processor.GetType().Name),
            settings.ProcessorId,
            settings.WorkerId);

        return new ProcessorWorker(
            settings,
            client,
            processor,
            diagnostics,
            NullLoggerFactory.Instance,
            TimeProvider.System,
            ownsClient,
            gateway
        );
    }

    static IWorkerSession CreateFakeSession(IAsyncEnumerable<ProcessorControlMessage>? messages = null) {
        var session = A.Fake<IWorkerSession>();

        A.CallTo(() => session.SessionId).Returns(SessionId.From(Guid.NewGuid()));

        messages ??= AsyncEnumerable.Empty<ProcessorControlMessage>();

        A.CallTo(() => session.GetAsyncEnumerator(A<CancellationToken>._))
            .ReturnsLazily(call => messages.GetAsyncEnumerator(call.GetArgument<CancellationToken>(0)));

        return session;
    }

    #region . cleanup .

    [Test]
    public async Task cleanup_runs_on_normal_completion() {
        // Arrange
        var session = CreateFakeSession();
        var gateway = A.Fake<IProcessorGateway>();
        var client  = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));

        A.CallTo(() => gateway.ConnectAsync(A<ProcessorSettings>._, A<CancellationToken>._))
            .Returns(session);

        var worker = CreateWorker(gateway, ownsClient: true, client: client);

        // Act
        await worker.RunAsync(CancellationToken.None);

        // Assert
        A.CallTo(() => session.DisposeAsync()).MustHaveHappenedOnceExactly();
        A.CallTo(() => client.DisposeAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task cleanup_runs_on_cancellation() {
        // Arrange
        var cts = new CancellationTokenSource();

        var messages = new BlockingAsyncEnumerable<ProcessorControlMessage>();
        var session  = CreateFakeSession(messages);
        var gateway  = A.Fake<IProcessorGateway>();
        var client   = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));

        A.CallTo(() => gateway.ConnectAsync(A<ProcessorSettings>._, A<CancellationToken>._))
            .Returns(session);

        var worker = CreateWorker(gateway, ownsClient: true, client: client);

        // Act — cancel immediately after connect
        cts.Cancel();

        // RunAsync should propagate the OperationCanceledException through HandleShutdown
        // which re-throws. The finally block still runs Cleanup.
        try {
            await worker.RunAsync(cts.Token);
        }
        catch (OperationCanceledException) {
            // expected
        }

        // Assert
        A.CallTo(() => session.DisposeAsync()).MustHaveHappenedOnceExactly();
        A.CallTo(() => client.DisposeAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task cleanup_runs_on_processing_error() {
        // Arrange
        var processor = A.Fake<IRecordProcessor>();

        A.CallTo(() => processor.OnError(A<ErrorContext>._)).Returns(ValueTask.CompletedTask);

        var session = CreateFakeSession();
        var gateway = A.Fake<IProcessorGateway>();
        var client  = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));

        // Make ConnectAsync throw to simulate a connection error
        A.CallTo(() => gateway.ConnectAsync(A<ProcessorSettings>._, A<CancellationToken>._))
            .ThrowsAsync(new InvalidOperationException("connection failed"));

        var worker = CreateWorker(gateway, processor: processor, ownsClient: true, client: client);

        // Act
        try {
            await worker.RunAsync(CancellationToken.None);
        }
        catch (InvalidOperationException) {
            // expected — HandleShutdown re-throws
        }

        // Assert — cleanup still ran
        A.CallTo(() => client.DisposeAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task cleanup_disposes_all_resources_even_when_session_disposal_throws() {
        // Arrange
        var session = CreateFakeSession();
        var gateway = A.Fake<IProcessorGateway>();
        var client  = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));

        A.CallTo(() => gateway.ConnectAsync(A<ProcessorSettings>._, A<CancellationToken>._))
            .Returns(session);
        A.CallTo(() => session.DisposeAsync())
            .ThrowsAsync(new ObjectDisposedException("session"));

        var worker = CreateWorker(gateway, ownsClient: true, client: client);

        // Act
        var ex = await Should.ThrowAsync<AggregateException>(worker.RunAsync(CancellationToken.None));

        // Assert — client was still disposed despite session throwing
        A.CallTo(() => client.DisposeAsync()).MustHaveHappenedOnceExactly();
        ex.InnerExceptions.Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task cleanup_throws_aggregate_when_multiple_disposals_fail() {
        // Arrange
        var session = CreateFakeSession();
        var gateway = A.Fake<IProcessorGateway>();
        var client  = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));

        A.CallTo(() => gateway.ConnectAsync(A<ProcessorSettings>._, A<CancellationToken>._))
            .Returns(session);
        A.CallTo(() => session.DisposeAsync())
            .ThrowsAsync(new ObjectDisposedException("session"));
        A.CallTo(() => client.DisposeAsync())
            .ThrowsAsync(new ObjectDisposedException("client"));

        var worker = CreateWorker(gateway, ownsClient: true, client: client);

        // Act
        var ex = await Should.ThrowAsync<AggregateException>(worker.RunAsync(CancellationToken.None));

        // Assert — both errors collected
        ex.InnerExceptions.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task cleanup_is_idempotent() {
        // Arrange
        var session = CreateFakeSession();
        var gateway = A.Fake<IProcessorGateway>();
        var client  = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));

        A.CallTo(() => gateway.ConnectAsync(A<ProcessorSettings>._, A<CancellationToken>._))
            .Returns(session);

        var worker = CreateWorker(gateway, ownsClient: true, client: client);

        // Act — first call triggers cleanup via RunAsync
        await worker.RunAsync(CancellationToken.None);

        // Second call throws InvalidOperationException (started guard), not double-dispose
        await Should.ThrowAsync<InvalidOperationException>(worker.RunAsync(CancellationToken.None));

        // Assert — resources were only disposed once
        A.CallTo(() => session.DisposeAsync()).MustHaveHappenedOnceExactly();
        A.CallTo(() => client.DisposeAsync()).MustHaveHappenedOnceExactly();
    }

    #endregion

    #region . run_async guards .

    [Test]
    public async Task run_async_throws_if_called_twice() {
        // Arrange
        var session = CreateFakeSession();
        var gateway = A.Fake<IProcessorGateway>();

        A.CallTo(() => gateway.ConnectAsync(A<ProcessorSettings>._, A<CancellationToken>._))
            .Returns(session);

        var worker = CreateWorker(gateway);

        // Act
        await worker.RunAsync(CancellationToken.None);

        // Assert
        await Should.ThrowAsync<InvalidOperationException>(worker.RunAsync(CancellationToken.None));
    }

    #endregion

    #region . client ownership .

    [Test]
    public async Task client_not_disposed_when_not_owned() {
        // Arrange
        var session = CreateFakeSession();
        var gateway = A.Fake<IProcessorGateway>();
        var client  = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));

        A.CallTo(() => gateway.ConnectAsync(A<ProcessorSettings>._, A<CancellationToken>._))
            .Returns(session);

        var worker = CreateWorker(gateway, ownsClient: false, client: client);

        // Act
        await worker.RunAsync(CancellationToken.None);

        // Assert
        A.CallTo(() => client.DisposeAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task client_disposed_when_owned() {
        // Arrange
        var session = CreateFakeSession();
        var gateway = A.Fake<IProcessorGateway>();
        var client  = A.Fake<KurrentClient>(o => o.WithArgumentsForConstructor([new KurrentClientOptions()]));

        A.CallTo(() => gateway.ConnectAsync(A<ProcessorSettings>._, A<CancellationToken>._))
            .Returns(session);

        var worker = CreateWorker(gateway, ownsClient: true, client: client);

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

        var processor = A.Fake<IRecordProcessor>();
        A.CallTo(() => processor.OnComplete(A<CompleteContext>._))
            .Invokes(() => callOrder.Add("OnComplete"))
            .Returns(ValueTask.CompletedTask);

        var session = CreateFakeSession();
        A.CallTo(() => session.DisposeAsync())
            .Invokes(() => callOrder.Add("SessionDisposed"))
            .Returns(ValueTask.CompletedTask);

        var gateway = A.Fake<IProcessorGateway>();
        A.CallTo(() => gateway.ConnectAsync(A<ProcessorSettings>._, A<CancellationToken>._))
            .Returns(session);

        var worker = CreateWorker(gateway, processor: processor);

        // Act
        await worker.RunAsync(CancellationToken.None);

        // Assert — OnComplete called before session disposed
        callOrder.ShouldBe(["OnComplete", "SessionDisposed"]);
    }

    #endregion
}

#region Test helpers

class NoOpProcessor : IRecordProcessor {
    public ValueTask ProcessRecords(ProcessRecordsContext context) => ValueTask.CompletedTask;
}

/// <summary>
/// An async enumerable that blocks until cancelled.
/// Used to simulate in-progress processing that gets cancelled.
/// </summary>
class BlockingAsyncEnumerable<T> : IAsyncEnumerable<T> {
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default) =>
        new BlockingEnumerator(ct);

    class BlockingEnumerator(CancellationToken ct) : IAsyncEnumerator<T> {
        public T Current => default!;

        public async ValueTask<bool> MoveNextAsync() {
            try {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // propagate so the foreach exits
                throw;
            }
            return false;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

#endregion
