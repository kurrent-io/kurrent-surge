using Microsoft.Extensions.DependencyInjection;
using Talyn;

namespace Kurrent.Client.Tests.Talyn;

[Skip("Talyn is not used in Kurrent Client yet")]
public class TalynRegistryTests {
    #region Test Messages and Receivers

    record TestMessage(string Value);
    record TestRequest(string Value);
    record TestResponse(string Result);

    class TestMessageReceiver : IReceiver<TestMessage> {
        public List<TestMessage> ReceivedMessages { get; } = [];

        public ValueTask HandleAsync(TestMessage message, CancellationToken ct) {
            ReceivedMessages.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    class AnotherTestMessageReceiver : IReceiver<TestMessage> {
        public List<TestMessage> ReceivedMessages { get; } = [];

        public ValueTask HandleAsync(TestMessage message, CancellationToken ct) {
            ReceivedMessages.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    class TestRequestReceiver : IReceiver<TestRequest, TestResponse> {
        public ValueTask<TestResponse> HandleAsync(TestRequest request, CancellationToken ct) =>
            ValueTask.FromResult(new TestResponse($"Response to: {request.Value}"));
    }

    class AnotherTestRequestReceiver : IReceiver<TestRequest, TestResponse> {
        public ValueTask<TestResponse> HandleAsync(TestRequest request, CancellationToken ct) =>
            ValueTask.FromResult(new TestResponse($"Another response to: {request.Value}"));
    }

    class ThirdTestRequestReceiver : IReceiver<TestRequest, TestResponse> {
        public ValueTask<TestResponse> HandleAsync(TestRequest request, CancellationToken ct) =>
            ValueTask.FromResult(new TestResponse($"Third response to: {request.Value}"));
    }

    #endregion

    #region TalynRegistry Tests (Standalone - No DI)

    [Test]
    public void registry_returns_empty_when_no_receivers() {
        // Arrange
        var registry = new TalynRegistry();

        // Act
        var entries = registry.GetReceivers<TestMessage>();

        // Assert
        entries.Length.ShouldBe(0);
    }

    [Test]
    public void registry_connect_adds_receiver() {
        // Arrange
        var registry = new TalynRegistry();
        var receiver = new TestMessageReceiver();

        // Act
        registry.Connect(receiver);
        var entries = registry.GetReceivers<TestMessage>();

        // Assert
        entries.Length.ShouldBe(1);
        entries[0].Receiver.ShouldBeSameAs(receiver);
    }

    [Test]
    public void registry_connect_allows_multiple_receivers() {
        // Arrange
        var registry = new TalynRegistry();
        var receiver1 = new TestMessageReceiver();
        var receiver2 = new AnotherTestMessageReceiver();

        // Act
        registry.Connect(receiver1);
        registry.Connect(receiver2);
        var entries = registry.GetReceivers<TestMessage>();

        // Assert
        entries.Length.ShouldBe(2);
    }

    [Test]
    public void registry_disconnect_removes_receiver() {
        // Arrange
        var registry = new TalynRegistry();
        var receiver = new TestMessageReceiver();
        registry.Connect(receiver);

        // Act
        var disconnected = registry.Disconnect(receiver);
        var entries = registry.GetReceivers<TestMessage>();

        // Assert
        entries.Length.ShouldBe(0);
        disconnected.ShouldNotBeNull();
        disconnected.Receiver.ShouldBeSameAs(receiver);
        disconnected.IsDisconnected.ShouldBeTrue();
    }

    [Test]
    public void registry_disconnect_returns_null_when_receiver_not_connected() {
        // Arrange
        var registry = new TalynRegistry();
        var receiver = new TestMessageReceiver();

        // Act
        var disconnected = registry.Disconnect(receiver);
        var entries = registry.GetReceivers<TestMessage>();

        // Assert
        entries.Length.ShouldBe(0);
        disconnected.ShouldBeNull();
    }

    [Test]
    public void registry_clear_removes_all_receivers() {
        // Arrange
        var registry = new TalynRegistry();
        registry.Connect(new TestMessageReceiver());
        registry.Connect(new AnotherTestMessageReceiver());

        // Act
        registry.Clear();

        // Assert
        registry.GetReceivers<TestMessage>().Length.ShouldBe(0);
    }

    [Test]
    public void registry_request_receiver_connect_and_get() {
        // Arrange
        var registry = new TalynRegistry();
        var receiver = new TestRequestReceiver();

        // Act
        registry.Connect<TestRequest, TestResponse>(receiver);
        var entries = registry.GetReceivers<TestRequest, TestResponse>();

        // Assert
        entries.Length.ShouldBe(1);
        entries[0].Receiver.ShouldBeSameAs(receiver);
    }

    [Test]
    public void registry_request_receiver_disconnect() {
        // Arrange
        var registry = new TalynRegistry();
        var receiver = new TestRequestReceiver();
        registry.Connect<TestRequest, TestResponse>(receiver);

        // Act
        var disconnected = registry.Disconnect<TestRequest, TestResponse>(receiver);
        var entries = registry.GetReceivers<TestRequest, TestResponse>();

        // Assert
        entries.Length.ShouldBe(0);
        disconnected.ShouldNotBeNull();
        disconnected.IsDisconnected.ShouldBeTrue();
    }

    #endregion

    #region DI Integration Tests

    [Test]
    public async Task di_registered_receivers_are_resolved() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus.WithReceiver<TestMessage, TestMessageReceiver>());
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Act - invoke to verify receiver is wired up
        var receiver = provider.GetRequiredService<IReceiver<TestMessage>>() as TestMessageReceiver;
        await bus.InvokeAsync(new TestMessage("Test"));

        // Assert
        receiver.ShouldNotBeNull();
        receiver.ReceivedMessages.Count.ShouldBe(1);

        await bus.DisposeAsync();
    }

    [Test]
    public async Task di_multiple_receivers_are_resolved() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus
            .WithReceiver<TestMessage, TestMessageReceiver>()
            .WithReceiver<TestMessage, AnotherTestMessageReceiver>());
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Act
        var receivers = provider.GetServices<IReceiver<TestMessage>>().ToList();

        // Assert
        receivers.Count.ShouldBe(2);

        await bus.DisposeAsync();
    }

    [Test]
    public async Task di_request_receiver_is_resolved() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus.WithReceiver<TestRequest, TestResponse, TestRequestReceiver>());
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Act
        var receiver = provider.GetService<IReceiver<TestRequest, TestResponse>>();

        // Assert
        receiver.ShouldNotBeNull();
        receiver.ShouldBeOfType<TestRequestReceiver>();

        await bus.DisposeAsync();
    }

    [Test]
    public async Task di_request_receiver_returns_null_when_not_registered() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn();
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Act
        var receiver = provider.GetService<IReceiver<TestRequest, TestResponse>>();

        // Assert
        receiver.ShouldBeNull();

        await bus.DisposeAsync();
    }

    [Test]
    public async Task di_and_runtime_receivers_are_merged() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus.WithReceiver<TestMessage, TestMessageReceiver>());
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();
        var runtimeReceiver = new AnotherTestMessageReceiver();

        // Act - connect runtime receiver
        bus.Connect(runtimeReceiver);
        await bus.InvokeAsync(new TestMessage("Hello"));

        // Assert - both receivers should have received the message
        var diReceiver = provider.GetRequiredService<IReceiver<TestMessage>>() as TestMessageReceiver;
        diReceiver!.ReceivedMessages.Count.ShouldBe(1);
        runtimeReceiver.ReceivedMessages.Count.ShouldBe(1);

        await bus.DisposeAsync();
    }

    #endregion

    #region ServiceLifetime Tests

    [Test]
    public async Task singleton_returns_same_instance() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus.WithReceiver<TestMessage, TestMessageReceiver>(ServiceLifetime.Singleton));
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Act
        var receiver1 = provider.GetRequiredService<IReceiver<TestMessage>>();
        var receiver2 = provider.GetRequiredService<IReceiver<TestMessage>>();

        // Assert
        receiver1.ShouldBeSameAs(receiver2);

        await bus.DisposeAsync();
    }

    [Test]
    public async Task transient_returns_new_instance() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus.WithReceiver<TestMessage, TestMessageReceiver>(ServiceLifetime.Transient));
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Act
        var receiver1 = provider.GetRequiredService<IReceiver<TestMessage>>();
        var receiver2 = provider.GetRequiredService<IReceiver<TestMessage>>();

        // Assert
        receiver1.ShouldNotBeSameAs(receiver2);

        await bus.DisposeAsync();
    }

    #endregion

    #region TalynBus Integration Tests

    [Test]
    public async Task talyn_bus_uses_di_receivers() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus.WithReceiver<TestMessage, TestMessageReceiver>());
        await using var provider = services.BuildServiceProvider();

        var bus = provider.GetRequiredService<ITalynBus>();
        var receiver = provider.GetRequiredService<IReceiver<TestMessage>>() as TestMessageReceiver;

        // Act
        await bus.TransmitAsync(new TestMessage("Hello"));

        // Assert
        receiver!.ReceivedMessages.Count.ShouldBe(1);
        receiver.ReceivedMessages[0].Value.ShouldBe("Hello");

        await bus.DisposeAsync();
    }

    [Test]
    public async Task talyn_bus_uses_request_receivers() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus.WithReceiver<TestRequest, TestResponse, TestRequestReceiver>());
        await using var provider = services.BuildServiceProvider();

        var bus = provider.GetRequiredService<ITalynBus>();

        // Act
        var response = await bus.TransmitAsync<TestRequest, TestResponse>(new TestRequest("Test"));

        // Assert
        response.Result.ShouldBe("Response to: Test");

        await bus.DisposeAsync();
    }

    [Test]
    public async Task talyn_bus_transmits_to_multiple_receivers() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus
            .WithReceiver<TestMessage, TestMessageReceiver>()
            .WithReceiver<TestMessage, AnotherTestMessageReceiver>());
        await using var provider = services.BuildServiceProvider();

        var bus = provider.GetRequiredService<ITalynBus>();
        var receivers = provider.GetServices<IReceiver<TestMessage>>().ToList();

        // Act
        await bus.TransmitAsync(new TestMessage("Broadcast"));

        // Assert
        var receiver1 = receivers[0] as TestMessageReceiver;
        var receiver2 = receivers[1] as AnotherTestMessageReceiver;
        receiver1!.ReceivedMessages.Count.ShouldBe(1);
        receiver2!.ReceivedMessages.Count.ShouldBe(1);

        await bus.DisposeAsync();
    }

    [Test]
    public async Task talyn_bus_uses_runtime_connections() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn();
        await using var provider = services.BuildServiceProvider();

        var bus = provider.GetRequiredService<ITalynBus>();
        var runtimeReceiver = new TestMessageReceiver();
        bus.Connect(runtimeReceiver);

        // Act
        await bus.TransmitAsync(new TestMessage("Runtime"));

        // Assert
        runtimeReceiver.ReceivedMessages.Count.ShouldBe(1);
        runtimeReceiver.ReceivedMessages[0].Value.ShouldBe("Runtime");

        await bus.DisposeAsync();
    }

    [Test]
    public async Task talyn_bus_builder_configures_options() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => {
            bus.WithOptions(opt => opt.ChannelCapacity = 50);
            bus.WithReceiver<TestMessage, TestMessageReceiver>();
        });
        await using var provider = services.BuildServiceProvider();

        var bus = provider.GetRequiredService<ITalynBus>();
        var receiver = provider.GetRequiredService<IReceiver<TestMessage>>() as TestMessageReceiver;

        // Act
        await bus.TransmitAsync(new TestMessage("Test"));

        // Assert
        receiver!.ReceivedMessages.Count.ShouldBe(1);

        await bus.DisposeAsync();
    }

    #endregion

    #region Direct Invocation Tests

    [Test]
    public async Task invoke_request_executes_directly() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus.WithReceiver<TestRequest, TestResponse, TestRequestReceiver>());
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Act
        var response = await bus.InvokeAsync<TestRequest, TestResponse>(new TestRequest("Test"));

        // Assert
        response.Result.ShouldBe("Response to: Test");

        await bus.DisposeAsync();
    }

    [Test]
    public async Task invoke_message_executes_directly() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus.WithReceiver<TestMessage, TestMessageReceiver>());
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();
        var receiver = provider.GetRequiredService<IReceiver<TestMessage>>() as TestMessageReceiver;

        // Act
        await bus.InvokeAsync(new TestMessage("Direct"));

        // Assert
        receiver!.ReceivedMessages.Count.ShouldBe(1);
        receiver.ReceivedMessages[0].Value.ShouldBe("Direct");

        await bus.DisposeAsync();
    }

    [Test]
    public async Task invoke_multiple_receivers_executes_all() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus
            .WithReceiver<TestMessage, TestMessageReceiver>()
            .WithReceiver<TestMessage, AnotherTestMessageReceiver>());
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();
        var receivers = provider.GetServices<IReceiver<TestMessage>>().ToList();

        // Act
        await bus.InvokeAsync(new TestMessage("DirectMulti"));

        // Assert
        var receiver1 = receivers[0] as TestMessageReceiver;
        var receiver2 = receivers[1] as AnotherTestMessageReceiver;
        receiver1!.ReceivedMessages.Count.ShouldBe(1);
        receiver2!.ReceivedMessages.Count.ShouldBe(1);

        await bus.DisposeAsync();
    }

    [Test]
    public async Task invoke_with_no_receivers_returns_immediately() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn();
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Act & Assert - should not throw
        await bus.InvokeAsync(new TestMessage("NoReceiver"));

        await bus.DisposeAsync();
    }

    #endregion

    #region Broadcast Tests (Queued Scatter-Gather)

    [Test]
    public async Task broadcast_returns_empty_array_when_no_receivers() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn();
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Act
        var responses = await bus.BroadcastAsync<TestRequest, TestResponse>(new TestRequest("Test"));

        // Assert
        responses.ShouldBeEmpty();

        await bus.DisposeAsync();
    }

    [Test]
    public async Task broadcast_returns_single_response_from_one_receiver() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus.WithReceiver<TestRequest, TestResponse, TestRequestReceiver>());
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Act
        var responses = await bus.BroadcastAsync<TestRequest, TestResponse>(new TestRequest("Test"));

        // Assert
        responses.Length.ShouldBe(1);
        responses[0].Result.Response.Result.ShouldBe("Response to: Test");

        await bus.DisposeAsync();
    }

    [Test]
    public async Task broadcast_returns_all_responses_from_multiple_receivers() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus
            .WithReceiver<TestRequest, TestResponse, TestRequestReceiver>()
            .WithReceiver<TestRequest, TestResponse, AnotherTestRequestReceiver>()
            .WithReceiver<TestRequest, TestResponse, ThirdTestRequestReceiver>());
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Act
        var responses = await bus.BroadcastAsync<TestRequest, TestResponse>(new TestRequest("Hello"));

        // Assert
        responses.Length.ShouldBe(3);
        var results = responses.Select(r => r.Result.Response.Result).ToList();
        results.ShouldContain("Response to: Hello");
        results.ShouldContain("Another response to: Hello");
        results.ShouldContain("Third response to: Hello");

        await bus.DisposeAsync();
    }

    #endregion

    #region Scatter Tests (Direct Scatter-Gather)

    [Test]
    public async Task scatter_returns_empty_array_when_no_receivers() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn();
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Act
        var responses = await bus.ScatterAsync<TestRequest, TestResponse>(new TestRequest("Test"));

        // Assert
        responses.ShouldBeEmpty();

        await bus.DisposeAsync();
    }

    [Test]
    public async Task scatter_returns_single_response_from_one_receiver() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus.WithReceiver<TestRequest, TestResponse, TestRequestReceiver>());
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Act
        var responses = await bus.ScatterAsync<TestRequest, TestResponse>(new TestRequest("Direct"));

        // Assert
        responses.Length.ShouldBe(1);
        responses[0].Result.Response.Result.ShouldBe("Response to: Direct");

        await bus.DisposeAsync();
    }

    [Test]
    public async Task scatter_returns_all_responses_from_multiple_receivers() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus
            .WithReceiver<TestRequest, TestResponse, TestRequestReceiver>()
            .WithReceiver<TestRequest, TestResponse, AnotherTestRequestReceiver>()
            .WithReceiver<TestRequest, TestResponse, ThirdTestRequestReceiver>());
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Act
        var responses = await bus.ScatterAsync<TestRequest, TestResponse>(new TestRequest("World"));

        // Assert
        responses.Length.ShouldBe(3);
        var results = responses.Select(r => r.Result.Response.Result).ToList();
        results.ShouldContain("Response to: World");
        results.ShouldContain("Another response to: World");
        results.ShouldContain("Third response to: World");

        await bus.DisposeAsync();
    }

    [Test]
    public async Task scatter_with_runtime_receivers() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn(bus => bus.WithReceiver<TestRequest, TestResponse, TestRequestReceiver>());
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        // Add runtime receiver
        var runtimeReceiver = new AnotherTestRequestReceiver();
        bus.Connect<TestRequest, TestResponse>(runtimeReceiver);

        // Act
        var responses = await bus.ScatterAsync<TestRequest, TestResponse>(new TestRequest("Mixed"));

        // Assert
        responses.Length.ShouldBe(2);
        var results = responses.Select(r => r.Result.Response.Result).ToList();
        results.ShouldContain("Response to: Mixed");
        results.ShouldContain("Another response to: Mixed");

        await bus.DisposeAsync();
    }

    #endregion

    #region Disconnect Tests

    [Test]
    public async Task disconnect_prevents_receiver_from_receiving_messages() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn();
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();
        var receiver = new TestMessageReceiver();
        bus.Connect(receiver);

        // Act
        await bus.InvokeAsync(new TestMessage("Before"));
        bus.Disconnect(receiver);
        await bus.InvokeAsync(new TestMessage("After"));

        // Assert
        receiver.ReceivedMessages.Count.ShouldBe(1);
        receiver.ReceivedMessages[0].Value.ShouldBe("Before");

        await bus.DisposeAsync();
    }

    [Test]
    public async Task disconnect_async_waits_for_active_execution() {
        // Arrange
        var services = new ServiceCollection();
        services.AddTalyn();
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ITalynBus>();

        var executionStarted = new TaskCompletionSource();
        var continueExecution = new TaskCompletionSource();
        var receiver = new LambdaReceiver<TestMessage>(async (msg, ct) => {
            executionStarted.SetResult();
            await continueExecution.Task;
        });
        bus.Connect(receiver);

        // Act - start processing, then disconnect while processing
        var invokeTask = bus.InvokeAsync(new TestMessage("Test")).AsTask();
        await executionStarted.Task;

        var disconnectTask = bus.DisconnectAsync(receiver).AsTask();
        await Task.Delay(50); // Give time for disconnect to start waiting
        disconnectTask.IsCompleted.ShouldBeFalse(); // Should be waiting

        continueExecution.SetResult();
        await disconnectTask;
        await invokeTask;

        await bus.DisposeAsync();
    }

    #endregion

    #region Standalone Builder Tests

    [Test]
    public async Task standalone_builder_creates_working_bus() {
        // Arrange & Act
        var receiver = new TestMessageReceiver();
        await using var bus = TalynBus.Create()
            .WithReceiver(receiver)
            .Build();

        await bus.InvokeAsync(new TestMessage("Standalone"));

        // Assert
        receiver.ReceivedMessages.Count.ShouldBe(1);
        receiver.ReceivedMessages[0].Value.ShouldBe("Standalone");
    }

    [Test]
    public async Task standalone_builder_with_lambda_handler() {
        // Arrange
        var received = new List<TestMessage>();
        await using var bus = TalynBus.Create()
            .WithReceiver<TestMessage>(async (msg, ct) => received.Add(msg))
            .Build();

        // Act
        await bus.InvokeAsync(new TestMessage("Lambda"));

        // Assert
        received.Count.ShouldBe(1);
        received[0].Value.ShouldBe("Lambda");
    }

    [Test]
    public async Task standalone_builder_with_request_handler() {
        // Arrange
        await using var bus = TalynBus.Create()
            .WithReceiver<TestRequest, TestResponse>(async (req, ct) =>
                new TestResponse($"Handled: {req.Value}"))
            .Build();

        // Act
        var response = await bus.InvokeAsync<TestRequest, TestResponse>(new TestRequest("Query"));

        // Assert
        response.Result.ShouldBe("Handled: Query");
    }

    [Test]
    public async Task standalone_builder_with_options() {
        // Arrange
        var receiver = new TestMessageReceiver();
        await using var bus = TalynBus.Create()
            .WithOptions(new TalynOptions { ChannelCapacity = 10 })
            .WithReceiver(receiver)
            .Build();

        // Act
        await bus.InvokeAsync(new TestMessage("WithOptions"));

        // Assert
        receiver.ReceivedMessages.Count.ShouldBe(1);
    }

    [Test]
    public async Task standalone_builder_with_sync_handler() {
        // Arrange
        var received = new List<TestMessage>();
        await using var bus = TalynBus.Create()
            .WithReceiver<TestMessage>(msg => received.Add(msg))
            .Build();

        // Act
        await bus.InvokeAsync(new TestMessage("Sync"));

        // Assert
        received.Count.ShouldBe(1);
    }

    [Test]
    public async Task standalone_builder_with_state_handler() {
        // Arrange
        var state = new List<string>();
        await using var bus = TalynBus.Create()
            .WithReceiver<TestMessage, List<string>>(state, static (s, msg, ct) => {
                s.Add(msg.Value);
                return ValueTask.CompletedTask;
            })
            .Build();

        // Act
        await bus.InvokeAsync(new TestMessage("StateHandler"));

        // Assert
        state.Count.ShouldBe(1);
        state[0].ShouldBe("StateHandler");
    }

    #endregion

    #region Execution Mode Tests

    [Test]
    public async Task scatter_sequential_maintains_order() {
        // Arrange
        var order = new List<int>();
        await using var bus = TalynBus.Create()
            .WithReceiver<TestRequest, TestResponse>(async (req, ct) => {
                order.Add(1);
                await Task.Delay(10, ct);
                return new TestResponse("First");
            })
            .WithReceiver<TestRequest, TestResponse>(async (req, ct) => {
                order.Add(2);
                await Task.Delay(10, ct);
                return new TestResponse("Second");
            })
            .Build();

        // Act
        var responses = await bus.ScatterAsync<TestRequest, TestResponse>(
            new TestRequest("Test"), ExecutionMode.Sequential);

        // Assert
        responses.Length.ShouldBe(2);
        order.ShouldBe([1, 2]);
    }

    [Test]
    public async Task scatter_concurrent_executes_in_parallel() {
        // Arrange
        var concurrencyCount = 0;
        var maxConcurrency = 0;
        var lockObj = new object();

        await using var bus = TalynBus.Create()
            .WithReceiver<TestRequest, TestResponse>(async (req, ct) => {
                lock (lockObj) {
                    concurrencyCount++;
                    maxConcurrency = Math.Max(maxConcurrency, concurrencyCount);
                }
                await Task.Delay(50, ct);
                lock (lockObj) { concurrencyCount--; }
                return new TestResponse("First");
            })
            .WithReceiver<TestRequest, TestResponse>(async (req, ct) => {
                lock (lockObj) {
                    concurrencyCount++;
                    maxConcurrency = Math.Max(maxConcurrency, concurrencyCount);
                }
                await Task.Delay(50, ct);
                lock (lockObj) { concurrencyCount--; }
                return new TestResponse("Second");
            })
            .Build();

        // Act
        var responses = await bus.ScatterAsync<TestRequest, TestResponse>(
            new TestRequest("Test"), ExecutionMode.Concurrent);

        // Assert
        responses.Length.ShouldBe(2);
        maxConcurrency.ShouldBe(2); // Both should run concurrently
    }

    #endregion
}
