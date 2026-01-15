// using Microsoft.Extensions.DependencyInjection;
// using Talyn;
//
// namespace Kurrent.Client.Tests.Talyn;
//
// public class TalynBehaviorTests {
//     #region Test Messages and Receivers
//
//     record TestMessage(string Value);
//     record TestRequest(string Value);
//     record TestResponse(string Result);
//
//     class TestMessageReceiver : IReceiver<TestMessage> {
//         public List<TestMessage> ReceivedMessages { get; } = [];
//
//         public ValueTask HandleAsync(TestMessage message, CancellationToken ct) {
//             ReceivedMessages.Add(message);
//             return ValueTask.CompletedTask;
//         }
//     }
//
//     class TestRequestReceiver : IReceiver<TestRequest, TestResponse> {
//         public ValueTask<TestResponse> HandleAsync(TestRequest request, CancellationToken ct) =>
//             ValueTask.FromResult(new TestResponse($"Response: {request.Value}"));
//     }
//
//     #endregion
//
//     #region Test Behaviors
//
//     class CapturingBehavior<T> : ITalynBehavior<T> {
//         public List<T> CapturedMessages { get; } = [];
//         public int BeforeCount { get; private set; }
//         public int AfterCount { get; private set; }
//
//         public async ValueTask HandleAsync(T message, BehaviorDelegate next, CancellationToken ct) {
//             BeforeCount++;
//             CapturedMessages.Add(message);
//             await next(ct);
//             AfterCount++;
//         }
//     }
//
//     class CapturingRequestBehavior<T, TResponse> : ITalynBehavior<T, TResponse> {
//         public List<T> CapturedMessages { get; } = [];
//         public List<TResponse> CapturedResponses { get; } = [];
//         public int BeforeCount { get; private set; }
//         public int AfterCount { get; private set; }
//
//         public async ValueTask<TResponse> HandleAsync(T message, BehaviorDelegate<TResponse> next, CancellationToken ct) {
//             BeforeCount++;
//             CapturedMessages.Add(message);
//             var response = await next(ct);
//             CapturedResponses.Add(response);
//             AfterCount++;
//             return response;
//         }
//     }
//
//     class TransformingBehavior<T, TResponse> : ITalynBehavior<T, TResponse>
//         where TResponse : TestResponse {
//         public async ValueTask<TResponse> HandleAsync(T message, BehaviorDelegate<TResponse> next, CancellationToken ct) {
//             var response = await next(ct);
//             return (TResponse)(object)new TestResponse($"{response.Result}+enriched");
//         }
//     }
//
//     class ShortCircuitBehavior<T> : ITalynBehavior<T> {
//         public bool WasCalled { get; private set; }
//
//         public ValueTask HandleAsync(T message, BehaviorDelegate next, CancellationToken ct) {
//             WasCalled = true;
//             // Don't call next - short-circuit the pipeline
//             return ValueTask.CompletedTask;
//         }
//     }
//
//     class OrderTrackingBehavior<T> : ITalynBehavior<T> {
//         readonly List<string> _order;
//         readonly string _name;
//
//         public OrderTrackingBehavior(List<string> order, string name) {
//             _order = order;
//             _name = name;
//         }
//
//         public async ValueTask HandleAsync(T message, BehaviorDelegate next, CancellationToken ct) {
//             _order.Add($"{_name}:before");
//             await next(ct);
//             _order.Add($"{_name}:after");
//         }
//     }
//
//     class CapturingBroadcastBehavior<T, TResponse> : ITalynBroadcastBehavior<T, TResponse> {
//         public List<T> CapturedMessages { get; } = [];
//         public List<ReceiverReply<TResponse>[]> CapturedReplies { get; } = [];
//
//         public async ValueTask<ReceiverReply<TResponse>[]> HandleAsync(
//             T message,
//             BehaviorDelegate<ReceiverReply<TResponse>[]> next,
//             CancellationToken ct
//         ) {
//             CapturedMessages.Add(message);
//             var replies = await next(ct);
//             CapturedReplies.Add(replies);
//             return replies;
//         }
//     }
//
//     #endregion
//
//     #region Fire-and-Forget Behavior Tests (Standalone)
//
//     [Test]
//     public async Task behavior_receives_typed_message() {
//         // Arrange
//         var behavior = new CapturingBehavior<TestMessage>();
//         var receiver = new TestMessageReceiver();
//
//         await using var bus = TalynBus.Create()
//             .UseBehavior(behavior)
//             .WithReceiver(receiver)
//             .Build();
//
//         // Act
//         await bus.InvokeAsync(new TestMessage("Hello"));
//
//         // Assert
//         behavior.CapturedMessages.Count.ShouldBe(1);
//         behavior.CapturedMessages[0].Value.ShouldBe("Hello");
//         receiver.ReceivedMessages.Count.ShouldBe(1);
//     }
//
//     [Test]
//     public async Task behavior_executes_before_and_after_receivers() {
//         // Arrange
//         var behavior = new CapturingBehavior<TestMessage>();
//         var receiver = new TestMessageReceiver();
//
//         await using var bus = TalynBus.Create()
//             .UseBehavior(behavior)
//             .WithReceiver(receiver)
//             .Build();
//
//         // Act
//         await bus.InvokeAsync(new TestMessage("Test"));
//
//         // Assert
//         behavior.BeforeCount.ShouldBe(1);
//         behavior.AfterCount.ShouldBe(1);
//     }
//
//     [Test]
//     public async Task behavior_can_short_circuit_pipeline() {
//         // Arrange
//         var shortCircuit = new ShortCircuitBehavior<TestMessage>();
//         var receiver = new TestMessageReceiver();
//
//         await using var bus = TalynBus.Create()
//             .UseBehavior(shortCircuit)
//             .WithReceiver(receiver)
//             .Build();
//
//         // Act
//         await bus.InvokeAsync(new TestMessage("Test"));
//
//         // Assert
//         shortCircuit.WasCalled.ShouldBeTrue();
//         receiver.ReceivedMessages.Count.ShouldBe(0); // Receiver should not be called
//     }
//
//     [Test]
//     public async Task multiple_behaviors_execute_in_order() {
//         // Arrange
//         var order = new List<string>();
//         var behavior1 = new OrderTrackingBehavior<TestMessage>(order, "B1");
//         var behavior2 = new OrderTrackingBehavior<TestMessage>(order, "B2");
//         var receiver = new TestMessageReceiver();
//
//         await using var bus = TalynBus.Create()
//             .UseBehavior(behavior1)
//             .UseBehavior(behavior2)
//             .WithReceiver(receiver)
//             .Build();
//
//         // Act
//         await bus.InvokeAsync(new TestMessage("Test"));
//
//         // Assert - first registered = outermost wrapper
//         order.ShouldBe(["B1:before", "B2:before", "B2:after", "B1:after"]);
//     }
//
//     [Test]
//     public async Task lambda_behavior_works() {
//         // Arrange
//         var capturedMessages = new List<TestMessage>();
//         var receiver = new TestMessageReceiver();
//
//         await using var bus = TalynBus.Create()
//             .UseBehavior<TestMessage>(async (msg, next, ct) => {
//                 capturedMessages.Add(msg);
//                 await next(ct);
//             })
//             .WithReceiver(receiver)
//             .Build();
//
//         // Act
//         await bus.InvokeAsync(new TestMessage("Lambda"));
//
//         // Assert
//         capturedMessages.Count.ShouldBe(1);
//         capturedMessages[0].Value.ShouldBe("Lambda");
//         receiver.ReceivedMessages.Count.ShouldBe(1);
//     }
//
//     [Test]
//     public async Task no_behavior_fast_path_works() {
//         // Arrange - no behaviors, just receivers
//         var receiver = new TestMessageReceiver();
//
//         await using var bus = TalynBus.Create()
//             .WithReceiver(receiver)
//             .Build();
//
//         // Act
//         await bus.InvokeAsync(new TestMessage("FastPath"));
//
//         // Assert
//         receiver.ReceivedMessages.Count.ShouldBe(1);
//     }
//
//     #endregion
//
//     #region Request/Response Behavior Tests (Standalone)
//
//     [Test]
//     public async Task request_behavior_receives_message_and_response() {
//         // Arrange
//         var behavior = new CapturingRequestBehavior<TestRequest, TestResponse>();
//
//         await using var bus = TalynBus.Create()
//             .UseBehavior(behavior)
//             .WithReceiver<TestRequest, TestResponse>(async (req, ct) =>
//                 new TestResponse($"Result: {req.Value}"))
//             .Build();
//
//         // Act
//         var response = await bus.InvokeAsync<TestRequest, TestResponse>(new TestRequest("Query"));
//
//         // Assert
//         behavior.CapturedMessages.Count.ShouldBe(1);
//         behavior.CapturedMessages[0].Value.ShouldBe("Query");
//         behavior.CapturedResponses.Count.ShouldBe(1);
//         response.Result.ShouldBe("Result: Query");
//     }
//
//     [Test]
//     public async Task request_behavior_can_transform_response() {
//         // Arrange
//         await using var bus = TalynBus.Create()
//             .UseBehavior<TestRequest, TestResponse>(async (msg, next, ct) => {
//                 var response = await next(ct);
//                 return new TestResponse($"{response.Result}+transformed");
//             })
//             .WithReceiver<TestRequest, TestResponse>(async (req, ct) =>
//                 new TestResponse("original"))
//             .Build();
//
//         // Act
//         var response = await bus.InvokeAsync<TestRequest, TestResponse>(new TestRequest("Test"));
//
//         // Assert
//         response.Result.ShouldBe("original+transformed");
//     }
//
//     [Test]
//     public async Task scatter_with_simple_behavior() {
//         // Arrange
//         var behavior = new CapturingRequestBehavior<TestRequest, TestResponse>();
//
//         await using var bus = TalynBus.Create()
//             .UseBehavior(behavior)
//             .WithReceiver<TestRequest, TestResponse>(async (req, ct) =>
//                 new TestResponse($"Receiver1: {req.Value}"))
//             .WithReceiver<TestRequest, TestResponse>(async (req, ct) =>
//                 new TestResponse($"Receiver2: {req.Value}"))
//             .Build();
//
//         // Act
//         var responses = await bus.ScatterAsync<TestRequest, TestResponse>(new TestRequest("Test"));
//
//         // Assert
//         behavior.CapturedMessages.Count.ShouldBe(1);
//         responses.Length.ShouldBe(2);
//     }
//
//     #endregion
//
//     #region Broadcast Behavior Tests (Standalone)
//
//     [Test]
//     public async Task broadcast_behavior_receives_all_replies() {
//         // Arrange
//         var behavior = new CapturingBroadcastBehavior<TestRequest, TestResponse>();
//
//         await using var bus = TalynBus.Create()
//             .UseBroadcastBehavior(behavior)
//             .WithReceiver<TestRequest, TestResponse>(async (req, ct) =>
//                 new TestResponse($"R1: {req.Value}"))
//             .WithReceiver<TestRequest, TestResponse>(async (req, ct) =>
//                 new TestResponse($"R2: {req.Value}"))
//             .Build();
//
//         // Act
//         var responses = await bus.ScatterAsync<TestRequest, TestResponse>(new TestRequest("Test"));
//
//         // Assert
//         behavior.CapturedMessages.Count.ShouldBe(1);
//         behavior.CapturedReplies.Count.ShouldBe(1);
//         behavior.CapturedReplies[0].Length.ShouldBe(2);
//         responses.Length.ShouldBe(2);
//     }
//
//     [Test]
//     public async Task lambda_broadcast_behavior_works() {
//         // Arrange
//         var capturedReplies = new List<ReceiverReply<TestResponse>[]>();
//
//         await using var bus = TalynBus.Create()
//             .UseBroadcastBehavior<TestRequest, TestResponse>(async (msg, next, ct) => {
//                 var replies = await next(ct);
//                 capturedReplies.Add(replies);
//                 return replies;
//             })
//             .WithReceiver<TestRequest, TestResponse>(async (req, ct) =>
//                 new TestResponse($"Reply: {req.Value}"))
//             .Build();
//
//         // Act
//         var responses = await bus.ScatterAsync<TestRequest, TestResponse>(new TestRequest("Test"));
//
//         // Assert
//         capturedReplies.Count.ShouldBe(1);
//         capturedReplies[0].Length.ShouldBe(1);
//     }
//
//     #endregion
//
//     #region DI Integration Tests
//
//     [Test]
//     public async Task di_registered_behavior_is_resolved() {
//         // Arrange
//         var services = new ServiceCollection();
//         services.AddTalyn(bus => bus
//             .UseBehavior<TestMessage, CapturingBehavior<TestMessage>>()
//             .WithReceiver<TestMessage, TestMessageReceiver>());
//         await using var provider = services.BuildServiceProvider();
//         var bus = provider.GetRequiredService<ITalynBus>();
//         var behavior = provider.GetRequiredService<ITalynBehavior<TestMessage>>() as CapturingBehavior<TestMessage>;
//
//         // Act
//         await bus.InvokeAsync(new TestMessage("DI"));
//
//         // Assert
//         behavior.ShouldNotBeNull();
//         behavior.CapturedMessages.Count.ShouldBe(1);
//
//         await bus.DisposeAsync();
//     }
//
//     [Test]
//     public async Task di_lambda_behavior_works() {
//         // Arrange
//         var capturedMessages = new List<TestMessage>();
//         var services = new ServiceCollection();
//         services.AddTalyn(bus => bus
//             .UseBehavior<TestMessage>(async (msg, next, ct) => {
//                 capturedMessages.Add(msg);
//                 await next(ct);
//             })
//             .WithReceiver<TestMessage, TestMessageReceiver>());
//         await using var provider = services.BuildServiceProvider();
//         var bus = provider.GetRequiredService<ITalynBus>();
//
//         // Act
//         await bus.InvokeAsync(new TestMessage("Lambda"));
//
//         // Assert
//         capturedMessages.Count.ShouldBe(1);
//
//         await bus.DisposeAsync();
//     }
//
//     [Test]
//     public async Task di_request_behavior_works() {
//         // Arrange
//         var services = new ServiceCollection();
//         services.AddTalyn(bus => bus
//             .UseBehavior<TestRequest, TestResponse, CapturingRequestBehavior<TestRequest, TestResponse>>()
//             .WithReceiver<TestRequest, TestResponse, TestRequestReceiver>());
//         await using var provider = services.BuildServiceProvider();
//         var bus = provider.GetRequiredService<ITalynBus>();
//         var behavior = provider.GetRequiredService<ITalynBehavior<TestRequest, TestResponse>>()
//             as CapturingRequestBehavior<TestRequest, TestResponse>;
//
//         // Act
//         var response = await bus.InvokeAsync<TestRequest, TestResponse>(new TestRequest("DI"));
//
//         // Assert
//         behavior.ShouldNotBeNull();
//         behavior.CapturedMessages.Count.ShouldBe(1);
//         response.Result.ShouldBe("Response: DI");
//
//         await bus.DisposeAsync();
//     }
//
//     [Test]
//     public async Task di_broadcast_behavior_works() {
//         // Arrange
//         var services = new ServiceCollection();
//         services.AddTalyn(bus => bus
//             .UseBroadcastBehavior<TestRequest, TestResponse,
//                 CapturingBroadcastBehavior<TestRequest, TestResponse>>()
//             .WithReceiver<TestRequest, TestResponse, TestRequestReceiver>());
//         await using var provider = services.BuildServiceProvider();
//         var bus = provider.GetRequiredService<ITalynBus>();
//         var behavior = provider.GetRequiredService<ITalynBroadcastBehavior<TestRequest, TestResponse>>()
//             as CapturingBroadcastBehavior<TestRequest, TestResponse>;
//
//         // Act
//         var responses = await bus.ScatterAsync<TestRequest, TestResponse>(new TestRequest("DI"));
//
//         // Assert
//         behavior.ShouldNotBeNull();
//         behavior.CapturedMessages.Count.ShouldBe(1);
//         responses.Length.ShouldBe(1);
//
//         await bus.DisposeAsync();
//     }
//
//     #endregion
//
//     #region TransmitAsync Behavior Tests
//
//     [Test]
//     public async Task transmit_with_behavior_queues_correctly() {
//         // Arrange
//         var behavior = new CapturingBehavior<TestMessage>();
//         var receiver = new TestMessageReceiver();
//
//         await using var bus = TalynBus.Create()
//             .UseBehavior(behavior)
//             .WithReceiver(receiver)
//             .Build();
//
//         // Act
//         await bus.TransmitAsync(new TestMessage("Queued"));
//
//         // Assert
//         behavior.CapturedMessages.Count.ShouldBe(1);
//         receiver.ReceivedMessages.Count.ShouldBe(1);
//     }
//
//     [Test]
//     public async Task broadcast_async_with_behavior_works() {
//         // Arrange
//         var behavior = new CapturingRequestBehavior<TestRequest, TestResponse>();
//
//         await using var bus = TalynBus.Create()
//             .UseBehavior(behavior)
//             .WithReceiver<TestRequest, TestResponse>(async (req, ct) =>
//                 new TestResponse($"R: {req.Value}"))
//             .Build();
//
//         // Act
//         var responses = await bus.BroadcastAsync<TestRequest, TestResponse>(new TestRequest("Test"));
//
//         // Assert
//         behavior.CapturedMessages.Count.ShouldBe(1);
//         responses.Length.ShouldBe(1);
//     }
//
//     #endregion
//
//     #region Mixed Middleware and Behavior Tests
//
//     [Test]
//     public async Task middleware_and_behavior_both_execute() {
//         // Arrange
//         var middlewareCalled = false;
//         var behaviorCalled = false;
//         var receiver = new TestMessageReceiver();
//
//         await using var bus = TalynBus.Create()
//             .Use(async (ctx, next) => {
//                 middlewareCalled = true;
//                 await next(ctx);
//             })
//             .UseBehavior<TestMessage>(async (msg, next, ct) => {
//                 behaviorCalled = true;
//                 await next(ct);
//             })
//             .WithReceiver(receiver)
//             .Build();
//
//         // Act
//         await bus.InvokeAsync(new TestMessage("Test"));
//
//         // Assert
//         middlewareCalled.ShouldBeTrue();
//         behaviorCalled.ShouldBeTrue();
//         receiver.ReceivedMessages.Count.ShouldBe(1);
//     }
//
//     [Test]
//     public async Task middleware_wraps_behaviors() {
//         // Arrange
//         var order = new List<string>();
//         var receiver = new TestMessageReceiver();
//
//         await using var bus = TalynBus.Create()
//             .Use(async (ctx, next) => {
//                 order.Add("MW:before");
//                 await next(ctx);
//                 order.Add("MW:after");
//             })
//             .UseBehavior<TestMessage>(async (msg, next, ct) => {
//                 order.Add("BH:before");
//                 await next(ct);
//                 order.Add("BH:after");
//             })
//             .WithReceiver(receiver)
//             .Build();
//
//         // Act
//         await bus.InvokeAsync(new TestMessage("Test"));
//
//         // Assert - Middleware is outermost, then behaviors, then receivers
//         order.ShouldBe(["MW:before", "BH:before", "BH:after", "MW:after"]);
//     }
//
//     #endregion
//
//     #region Runtime Connect/Disconnect Behavior Tests
//
//     [Test]
//     public async Task runtime_connected_behavior_executes() {
//         // Arrange
//         var behavior = new CapturingBehavior<TestMessage>();
//         var receiver = new TestMessageReceiver();
//
//         await using var bus = TalynBus.Create()
//             .WithReceiver(receiver)
//             .Build();
//
//         // Act - connect behavior at runtime
//         bus.Connect(behavior);
//         await bus.InvokeAsync(new TestMessage("Runtime"));
//
//         // Assert
//         behavior.CapturedMessages.Count.ShouldBe(1);
//         receiver.ReceivedMessages.Count.ShouldBe(1);
//     }
//
//     [Test]
//     public async Task runtime_disconnected_behavior_does_not_execute() {
//         // Arrange
//         var behavior = new CapturingBehavior<TestMessage>();
//         var receiver = new TestMessageReceiver();
//
//         await using var bus = TalynBus.Create()
//             .UseBehavior(behavior)
//             .WithReceiver(receiver)
//             .Build();
//
//         // Act - invoke once, disconnect, invoke again
//         await bus.InvokeAsync(new TestMessage("Before"));
//         bus.Disconnect(behavior);
//         await bus.InvokeAsync(new TestMessage("After"));
//
//         // Assert
//         behavior.CapturedMessages.Count.ShouldBe(1);
//         behavior.CapturedMessages[0].Value.ShouldBe("Before");
//         receiver.ReceivedMessages.Count.ShouldBe(2); // Receiver gets both
//     }
//
//     #endregion
// }
