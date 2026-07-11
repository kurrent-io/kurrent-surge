using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kurrent.Surge.Mcp.Tests;

/// <summary>
/// Conformance tests for <see cref="DuckDBMcpTaskStore"/>, the DuckDB-backed
/// <see cref="IMcpTaskStore"/> adapter for ModelContextProtocol.Core 2.0. Mirrors the behavior of
/// the SDK's own reference <c>InMemoryMcpTaskStore</c>.
/// </summary>
[Category("Mcp")]
[Category("DuckDB")]
[Timeout(30_000)]
public class DuckDBMcpTaskStoreTests {
    [ClassDataSource<TempDuckDb>(Shared = SharedType.None)]
    public required TempDuckDb Db { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    // Deterministic clock so CreatedAt/LastUpdatedAt assertions are exact and reproducible.
    FakeTimeProvider Time { get; } = new();

    DuckDBMcpTaskStore NewStore() =>
        new(Db.ConnectionString, timeProvider: Time);

    [Test]
    public async ValueTask create_task_returns_working_status_with_clock_timestamps() {
        // Arrange
        using var store = NewStore();
        var now = Time.GetUtcNow();

        // Act
        var task = await store.CreateTaskAsync();

        // Assert
        await Assert.That(task.Status).IsEqualTo(McpTaskStatus.Working);
        await Assert.That(task.TaskId).IsNotNullOrEmpty();
        await Assert.That(task.CreatedAt).IsEqualTo(now);
        await Assert.That(task.LastUpdatedAt).IsEqualTo(now);
    }

    [Test]
    public async ValueTask get_task_returns_null_for_unknown_id() {
        // Arrange
        using var store = NewStore();
        var unknownId = Faker.Random.Guid().ToString();

        // Act
        var task = await store.GetTaskAsync(unknownId);

        // Assert
        await Assert.That(task).IsNull();
    }

    [Test]
    public async ValueTask get_task_returns_created_task() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateTaskAsync();

        // Act
        var fetched = await store.GetTaskAsync(created.TaskId);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.TaskId).IsEqualTo(created.TaskId);
        await Assert.That(fetched.Status).IsEqualTo(McpTaskStatus.Working);
    }

    [Test]
    public async ValueTask set_completed_transitions_to_completed_and_stores_result() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateTaskAsync();
        var result = JsonSerializer.SerializeToElement(new ResultDto(Faker.Lorem.Sentence()));
        var expectedRawText = result.GetRawText();

        // Act
        await store.SetCompletedAsync(created.TaskId, result);
        var fetched = await store.GetTaskAsync(created.TaskId);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Completed);
        await Assert.That(fetched.Result).IsNotNull();
        await Assert.That(fetched.Result!.Value.GetRawText()).IsEqualTo(expectedRawText);
    }

    [Test]
    public async ValueTask completed_task_ignores_subsequent_set_failed_call() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateTaskAsync();
        var result = JsonSerializer.SerializeToElement(new ResultDto(Faker.Lorem.Sentence()));
        var expectedRawText = result.GetRawText();
        await store.SetCompletedAsync(created.TaskId, result);
        var error = JsonSerializer.SerializeToElement(new ErrorDto(Faker.Lorem.Sentence()));

        // Act — a terminal task must ignore a later transition attempt rather than throw.
        await store.SetFailedAsync(created.TaskId, error);
        var fetched = await store.GetTaskAsync(created.TaskId);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Completed);
        await Assert.That(fetched.Result!.Value.GetRawText()).IsEqualTo(expectedRawText);
    }

    [Test]
    public async ValueTask set_completed_on_missing_task_throws() {
        // Arrange
        using var store = NewStore();
        var missingId = Faker.Random.Guid().ToString();
        var result = JsonSerializer.SerializeToElement(new ResultDto(Faker.Lorem.Sentence()));
        var expectedMessage = $"Task '{missingId}' not found.";

        // Act / Assert
        await Assert.That(async () => await store.SetCompletedAsync(missingId, result))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage(expectedMessage);
    }

    [Test]
    public async ValueTask set_failed_transitions_to_failed_and_stores_error() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateTaskAsync();
        var error = JsonSerializer.SerializeToElement(new ErrorDto(Faker.Lorem.Sentence()));
        var expectedRawText = error.GetRawText();

        // Act
        await store.SetFailedAsync(created.TaskId, error);
        var fetched = await store.GetTaskAsync(created.TaskId);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Failed);
        await Assert.That(fetched.Error).IsNotNull();
        await Assert.That(fetched.Error!.Value.GetRawText()).IsEqualTo(expectedRawText);
    }

    [Test]
    public async ValueTask set_cancelled_returns_true_for_working_task_and_transitions_to_cancelled() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateTaskAsync();

        // Act
        var cancelled = await store.SetCancelledAsync(created.TaskId);
        var fetched = await store.GetTaskAsync(created.TaskId);

        // Assert
        await Assert.That(cancelled).IsTrue();
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Cancelled);
    }

    [Test]
    public async ValueTask set_cancelled_returns_false_for_missing_task() {
        // Arrange
        using var store = NewStore();
        var missingId = Faker.Random.Guid().ToString();

        // Act
        var cancelled = await store.SetCancelledAsync(missingId);

        // Assert
        await Assert.That(cancelled).IsFalse();
    }

    [Test]
    public async ValueTask set_cancelled_returns_false_for_already_terminal_task() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateTaskAsync();
        var result = JsonSerializer.SerializeToElement(new ResultDto(Faker.Lorem.Sentence()));
        await store.SetCompletedAsync(created.TaskId, result);

        // Act
        var cancelled = await store.SetCancelledAsync(created.TaskId);
        var fetched = await store.GetTaskAsync(created.TaskId);

        // Assert
        await Assert.That(cancelled).IsFalse();
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Completed);
    }

    [Test]
    public async ValueTask set_input_requests_transitions_to_input_required_and_exposes_pending_requests() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateTaskAsync();
        var requestKey = Faker.Random.AlphaNumeric(8);
        var message = Faker.Lorem.Sentence();
        var inputRequests = new Dictionary<string, InputRequest> {
            [requestKey] = InputRequest.ForElicitation(new ElicitRequestParams { Message = message })
        };

        // Act
        await store.SetInputRequestsAsync(created.TaskId, inputRequests);
        var fetched = await store.GetTaskAsync(created.TaskId);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.InputRequired);
        await Assert.That(fetched.InputRequests).IsNotNull();
        await Assert.That(fetched.InputRequests!.ContainsKey(requestKey)).IsTrue();
        await Assert.That(fetched.InputRequests[requestKey].ElicitationParams?.Message).IsEqualTo(message);
    }

    [Test]
    public async ValueTask set_input_requests_on_missing_task_throws() {
        // Arrange
        using var store = NewStore();
        var missingId = Faker.Random.Guid().ToString();
        var inputRequests = new Dictionary<string, InputRequest> {
            [Faker.Random.AlphaNumeric(8)] = InputRequest.ForElicitation(new ElicitRequestParams { Message = Faker.Lorem.Sentence() })
        };
        var expectedMessage = $"Task '{missingId}' not found.";

        // Act / Assert
        await Assert.That(async () => await store.SetInputRequestsAsync(missingId, inputRequests))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage(expectedMessage);
    }

    [Test]
    public async ValueTask resolve_input_requests_removes_satisfied_request_and_returns_to_working() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateTaskAsync();
        var requestKey = Faker.Random.AlphaNumeric(8);
        var inputRequests = new Dictionary<string, InputRequest> {
            [requestKey] = InputRequest.ForElicitation(new ElicitRequestParams { Message = Faker.Lorem.Sentence() })
        };
        await store.SetInputRequestsAsync(created.TaskId, inputRequests);
        var inputResponses = new Dictionary<string, InputResponse> {
            [requestKey] = InputResponse.FromElicitResult(new ElicitResult { Action = "accept" })
        };

        // Act
        await store.ResolveInputRequestsAsync(created.TaskId, inputResponses);
        var fetched = await store.GetTaskAsync(created.TaskId);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Working);
        await Assert.That(fetched.InputRequests).IsNull();
    }

    [Test]
    public async ValueTask resolve_input_requests_raises_input_response_received_event() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateTaskAsync();
        var requestKey = Faker.Random.AlphaNumeric(8);
        var inputRequests = new Dictionary<string, InputRequest> {
            [requestKey] = InputRequest.ForElicitation(new ElicitRequestParams { Message = Faker.Lorem.Sentence() })
        };
        await store.SetInputRequestsAsync(created.TaskId, inputRequests);
        var response = InputResponse.FromElicitResult(new ElicitResult { Action = "accept" });
        var inputResponses = new Dictionary<string, InputResponse> { [requestKey] = response };
        var received = new List<InputResponseReceivedEventArgs>();
        store.InputResponseReceived += received.Add;

        // Act
        await store.ResolveInputRequestsAsync(created.TaskId, inputResponses);

        // Assert
        await Assert.That(received.Count).IsEqualTo(1);
        await Assert.That(received[0].TaskId).IsEqualTo(created.TaskId);
        await Assert.That(received[0].RequestId).IsEqualTo(requestKey);
    }

    [Test]
    public async ValueTask resolve_input_requests_on_terminal_task_does_not_raise_event() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateTaskAsync();
        var result = JsonSerializer.SerializeToElement(new ResultDto(Faker.Lorem.Sentence()));
        await store.SetCompletedAsync(created.TaskId, result);
        var inputResponses = new Dictionary<string, InputResponse> {
            [Faker.Random.AlphaNumeric(8)] = InputResponse.FromElicitResult(new ElicitResult { Action = "accept" })
        };
        var received = new List<InputResponseReceivedEventArgs>();
        store.InputResponseReceived += received.Add;

        // Act — dropped silently: a terminal task has no listener left to act on the response.
        await store.ResolveInputRequestsAsync(created.TaskId, inputResponses);

        // Assert
        await Assert.That(received.Count).IsEqualTo(0);
    }

    [Test]
    public async ValueTask concurrent_writes_to_same_task_all_succeed_without_conflict() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateTaskAsync();
        const int writers = 32;
        var keys = Enumerable.Range(0, writers).Select(_ => Faker.Random.AlphaNumeric(10)).Distinct().ToArray();

        // Act — fire many writers concurrently at the SAME task row. Each merges one distinct
        // input-request key. Under DuckDB's optimistic MVCC these transactions collide on the
        // row; the store must absorb the write-write conflicts (DuckDBErrorType.Transaction) via
        // its bounded retry rather than surface them to the caller, and no merge may be lost.
        var tasks = keys.Select(key => Task.Run(() =>
            store.SetInputRequestsAsync(created.TaskId, new Dictionary<string, InputRequest> {
                [key] = InputRequest.ForElicitation(new ElicitRequestParams { Message = Faker.Lorem.Sentence() })
            }))).ToArray();
        await Task.WhenAll(tasks);

        // Assert — no writer threw, the task is InputRequired, and every concurrent merge survived.
        var fetched = await store.GetTaskAsync(created.TaskId);
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.InputRequired);
        await Assert.That(fetched.InputRequests).IsNotNull();
        await Assert.That(fetched.InputRequests!.Count).IsEqualTo(keys.Length);
    }

    [Test]
    public async ValueTask add_duckdb_mcp_task_store_registers_singleton() {
        // Arrange
        var services = new ServiceCollection().AddDuckDBMcpTaskStore(Db.ConnectionString);
        using var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IMcpTaskStore>();
        var second = provider.GetRequiredService<IMcpTaskStore>();

        // Assert
        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsSameReferenceAs(first);
    }

    sealed record ResultDto(string Value);

    sealed record ErrorDto(string Message);
}
