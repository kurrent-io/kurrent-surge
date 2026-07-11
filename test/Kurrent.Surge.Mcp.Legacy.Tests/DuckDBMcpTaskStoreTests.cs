using Kurrent.Surge.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Kurrent.Surge.Mcp.Legacy.Tests;

/// <summary>
/// Conformance tests for <see cref="DuckDBMcpTaskStore"/> against the ModelContextProtocol.Core
/// 1.4.1 <see cref="IMcpTaskStore"/> contract — session isolation, result storage/retrieval,
/// status transitions, listing/pagination, and TTL clamping. Assertions mirror the reference
/// <c>InMemoryMcpTaskStore</c> conformance suite, including the exact exception messages emitted
/// by the adapter.
/// </summary>
[Category("Mcp")]
[Category("DuckDB")]
[Timeout(30_000)]
public class DuckDBMcpTaskStoreTests {
    [ClassDataSource<TempDuckDb>(Shared = SharedType.None)]
    public required TempDuckDb Db { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    // Deterministic clock so CreatedAt/TTL assertions are exact and reproducible.
    FakeTimeProvider Time { get; } = new();

    // Cleanup timer disabled by default — none of these tests exercise the background sweep.
    DuckDBMcpTaskStore NewStore(DuckDBTaskStoreOptions? options = null) =>
        new(Db.ConnectionString, options ?? new DuckDBTaskStoreOptions { CleanupInterval = Timeout.InfiniteTimeSpan }, Time);

    async ValueTask<McpTask> CreateTask(DuckDBMcpTaskStore store, string? sessionId = null, TimeSpan? timeToLive = null) =>
        await store.CreateTaskAsync(
            new McpTaskMetadata { TimeToLive = timeToLive },
            new RequestId(Faker.Random.AlphaNumeric(8)),
            new JsonRpcRequest { Method = "tools/call" },
            sessionId);

    sealed record SampleResult(string Message);

    [Test]
    public async ValueTask create_task_returns_working_task_with_id_and_created_at() {
        // Arrange
        using var store = NewStore();
        var now = Time.GetUtcNow();

        // Act
        var task = await CreateTask(store);

        // Assert
        await Assert.That(task.Status).IsEqualTo(McpTaskStatus.Working);
        await Assert.That(task.TaskId).IsNotNullOrEmpty();
        await Assert.That(task.CreatedAt).IsEqualTo(now);
    }

    [Test]
    public async ValueTask get_task_returns_null_for_unknown_id() {
        // Arrange
        using var store = NewStore();
        var unknownTaskId = Faker.Random.Guid().ToString();

        // Act
        var task = await store.GetTaskAsync(unknownTaskId);

        // Assert
        await Assert.That(task).IsNull();
    }

    [Test]
    public async ValueTask get_task_returns_the_created_task() {
        // Arrange
        using var store = NewStore();
        var created = await CreateTask(store);

        // Act
        var fetched = await store.GetTaskAsync(created.TaskId);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.TaskId).IsEqualTo(created.TaskId);
        await Assert.That(fetched.Status).IsEqualTo(created.Status);
    }

    [Test]
    public async ValueTask get_task_returns_null_when_caller_session_does_not_match() {
        // Arrange
        using var store = NewStore();
        var ownerSessionId = Faker.Random.AlphaNumeric(8);
        var callerSessionId = Faker.Random.AlphaNumeric(8);
        var created = await CreateTask(store, ownerSessionId);

        // Act
        var fetched = await store.GetTaskAsync(created.TaskId, callerSessionId);

        // Assert
        await Assert.That(fetched).IsNull();
    }

    [Test]
    public async ValueTask get_task_with_null_caller_session_bypasses_isolation() {
        // Arrange
        using var store = NewStore();
        var ownerSessionId = Faker.Random.AlphaNumeric(8);
        var created = await CreateTask(store, ownerSessionId);

        // Act — a null caller session is treated as "no isolation requested".
        var fetched = await store.GetTaskAsync(created.TaskId, sessionId: null);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.TaskId).IsEqualTo(created.TaskId);
    }

    [Test]
    public async ValueTask store_task_result_with_completed_status_stores_result_and_transitions() {
        // Arrange
        using var store = NewStore();
        var created = await CreateTask(store);
        var expectedMessage = Faker.Lorem.Sentence();
        var payload = JsonSerializer.SerializeToElement(new SampleResult(expectedMessage));

        // Act
        var updated = await store.StoreTaskResultAsync(created.TaskId, McpTaskStatus.Completed, payload);
        var stored = await store.GetTaskResultAsync(created.TaskId);

        // Assert
        await Assert.That(updated.Status).IsEqualTo(McpTaskStatus.Completed);
        var deserialized = stored.Deserialize<SampleResult>();
        await Assert.That(deserialized!.Message).IsEqualTo(expectedMessage);
    }

    [Test]
    public async ValueTask store_task_result_with_non_terminal_status_throws_argument_exception() {
        // Arrange
        using var store = NewStore();
        var created = await CreateTask(store);
        var payload = JsonSerializer.SerializeToElement(new SampleResult(Faker.Lorem.Sentence()));

        // Act
        ArgumentException? caught = null;
        try {
            await store.StoreTaskResultAsync(created.TaskId, McpTaskStatus.Working, payload);
        }
        catch (ArgumentException ex) {
            caught = ex;
        }

        // Assert
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("Status must be Completed or Failed.");
    }

    [Test]
    public async ValueTask store_task_result_on_already_terminal_task_throws_invalid_operation() {
        // Arrange
        using var store = NewStore();
        var created = await CreateTask(store);
        var payload = JsonSerializer.SerializeToElement(new SampleResult(Faker.Lorem.Sentence()));
        await store.StoreTaskResultAsync(created.TaskId, McpTaskStatus.Completed, payload);
        var expectedMessage = $"Cannot store result for task in terminal state: {McpTaskState.Completed}";

        // Act
        InvalidOperationException? caught = null;
        try {
            await store.StoreTaskResultAsync(created.TaskId, McpTaskStatus.Failed, payload);
        }
        catch (InvalidOperationException ex) {
            caught = ex;
        }

        // Assert
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).IsEqualTo(expectedMessage);
    }

    [Test]
    public async ValueTask store_task_result_on_missing_task_throws_task_not_found() {
        // Arrange
        using var store = NewStore();
        var missingTaskId = Faker.Random.Guid().ToString();
        var payload = JsonSerializer.SerializeToElement(new SampleResult(Faker.Lorem.Sentence()));
        var expectedMessage = $"Task not found: {missingTaskId}";

        // Act
        InvalidOperationException? caught = null;
        try {
            await store.StoreTaskResultAsync(missingTaskId, McpTaskStatus.Completed, payload);
        }
        catch (InvalidOperationException ex) {
            caught = ex;
        }

        // Assert
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).IsEqualTo(expectedMessage);
    }

    [Test]
    public async ValueTask get_task_result_on_missing_task_throws_task_not_found() {
        // Arrange
        using var store = NewStore();
        var missingTaskId = Faker.Random.Guid().ToString();
        var expectedMessage = $"Task not found: {missingTaskId}";

        // Act
        InvalidOperationException? caught = null;
        try {
            await store.GetTaskResultAsync(missingTaskId);
        }
        catch (InvalidOperationException ex) {
            caught = ex;
        }

        // Assert
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).IsEqualTo(expectedMessage);
    }

    [Test]
    public async ValueTask get_task_result_with_no_stored_result_throws_invalid_operation() {
        // Arrange
        using var store = NewStore();
        var created = await CreateTask(store);
        var expectedMessage = $"No result stored for task: {created.TaskId}";

        // Act
        InvalidOperationException? caught = null;
        try {
            await store.GetTaskResultAsync(created.TaskId);
        }
        catch (InvalidOperationException ex) {
            caught = ex;
        }

        // Assert
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).IsEqualTo(expectedMessage);
    }

    [Test]
    public async ValueTask get_task_result_with_null_caller_session_does_not_bypass_isolation() {
        // Arrange — GetTaskResultAsync compares sessions unconditionally; unlike every other
        // method, a null caller session does NOT bypass isolation.
        using var store = NewStore();
        var ownerSessionId = Faker.Random.AlphaNumeric(8);
        var created = await CreateTask(store, ownerSessionId);
        var payload = JsonSerializer.SerializeToElement(new SampleResult(Faker.Lorem.Sentence()));
        await store.StoreTaskResultAsync(created.TaskId, McpTaskStatus.Completed, payload, ownerSessionId);
        string? callerSessionId = null;
        var expectedMessage = $"Invalid sessionId: {callerSessionId} provided for {created.TaskId}";

        // Act
        InvalidOperationException? caught = null;
        try {
            await store.GetTaskResultAsync(created.TaskId, callerSessionId);
        }
        catch (InvalidOperationException ex) {
            caught = ex;
        }

        // Assert
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).IsEqualTo(expectedMessage);
    }

    [Test]
    public async ValueTask update_task_status_allows_terminal_to_non_terminal_transition() {
        // Arrange
        using var store = NewStore();
        var created = await CreateTask(store);
        await store.CancelTaskAsync(created.TaskId);

        // Act
        var updated = await store.UpdateTaskStatusAsync(created.TaskId, McpTaskStatus.Working, Faker.Lorem.Sentence());
        var fetched = await store.GetTaskAsync(created.TaskId);

        // Assert
        await Assert.That(updated.Status).IsEqualTo(McpTaskStatus.Working);
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Working);
    }

    [Test]
    public async ValueTask update_task_status_overwrites_status_message_with_null() {
        // Arrange
        using var store = NewStore();
        var created = await CreateTask(store);
        await store.UpdateTaskStatusAsync(created.TaskId, McpTaskStatus.Working, Faker.Lorem.Sentence());

        // Act
        var updated = await store.UpdateTaskStatusAsync(created.TaskId, McpTaskStatus.Working, null);

        // Assert
        await Assert.That(updated.StatusMessage).IsNull();
    }

    [Test]
    public async ValueTask update_task_status_on_missing_task_throws_task_not_found() {
        // Arrange
        using var store = NewStore();
        var missingTaskId = Faker.Random.Guid().ToString();
        var expectedMessage = $"Task not found: {missingTaskId}";

        // Act
        InvalidOperationException? caught = null;
        try {
            await store.UpdateTaskStatusAsync(missingTaskId, McpTaskStatus.Working, null);
        }
        catch (InvalidOperationException ex) {
            caught = ex;
        }

        // Assert
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).IsEqualTo(expectedMessage);
    }

    [Test]
    public async ValueTask cancel_task_transitions_working_task_to_cancelled() {
        // Arrange
        using var store = NewStore();
        var created = await CreateTask(store);

        // Act
        var cancelled = await store.CancelTaskAsync(created.TaskId);

        // Assert
        await Assert.That(cancelled.Status).IsEqualTo(McpTaskStatus.Cancelled);
    }

    [Test]
    public async ValueTask cancel_task_on_already_terminal_task_is_idempotent() {
        // Arrange
        using var store = NewStore();
        var created = await CreateTask(store);
        var payload = JsonSerializer.SerializeToElement(new SampleResult(Faker.Lorem.Sentence()));
        await store.StoreTaskResultAsync(created.TaskId, McpTaskStatus.Completed, payload);

        // Act — cancelling an already-terminal task must not throw or downgrade its status.
        var cancelled = await store.CancelTaskAsync(created.TaskId);

        // Assert
        await Assert.That(cancelled.Status).IsEqualTo(McpTaskStatus.Completed);
    }

    [Test]
    public async ValueTask cancel_task_on_missing_task_throws_task_not_found() {
        // Arrange
        using var store = NewStore();
        var missingTaskId = Faker.Random.Guid().ToString();
        var expectedMessage = $"Task not found: {missingTaskId}";

        // Act
        InvalidOperationException? caught = null;
        try {
            await store.CancelTaskAsync(missingTaskId);
        }
        catch (InvalidOperationException ex) {
            caught = ex;
        }

        // Assert
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).IsEqualTo(expectedMessage);
    }

    [Test]
    public async ValueTask list_tasks_returns_only_tasks_for_the_given_session() {
        // Arrange
        using var store = NewStore();
        var sessionA = Faker.Random.AlphaNumeric(8);
        var sessionB = Faker.Random.AlphaNumeric(8);
        var taskA1 = await CreateTask(store, sessionA);
        Time.Advance(TimeSpan.FromSeconds(1));
        var taskA2 = await CreateTask(store, sessionA);
        Time.Advance(TimeSpan.FromSeconds(1));
        await CreateTask(store, sessionB);
        var expectedIds = new List<string> { taskA1.TaskId, taskA2.TaskId };

        // Act
        var page = await store.ListTasksAsync(sessionId: sessionA);
        var actualIds = page.Tasks.Select(t => t.TaskId).ToList();

        // Assert
        await Assert.That(page.Tasks.Count).IsEqualTo(2);
        await Assert.That(actualIds).IsEquivalentTo(expectedIds);
    }

    [Test]
    public async ValueTask list_tasks_paginates_through_all_created_tasks() {
        // Arrange
        using var store = NewStore(new DuckDBTaskStoreOptions { PageSize = 2, CleanupInterval = Timeout.InfiniteTimeSpan });
        var created = new HashSet<string>();
        for (var i = 0; i < 5; i++) {
            var task = await CreateTask(store);
            created.Add(task.TaskId);
            Time.Advance(TimeSpan.FromSeconds(1));
        }

        // Act — walk every page following the cursor.
        var seen = new List<string>();
        string? cursor = null;
        do {
            var page = await store.ListTasksAsync(cursor: cursor);
            seen.AddRange(page.Tasks.Select(t => t.TaskId));
            cursor = page.NextCursor;
        } while (cursor is not null);

        // Assert — every task seen exactly once.
        await Assert.That(seen.Count).IsEqualTo(5);
        await Assert.That(seen.ToHashSet()).IsEquivalentTo(created);
    }

    [Test]
    public async ValueTask create_task_clamps_requested_ttl_to_max_time_to_live() {
        // Arrange
        var maxTimeToLive = TimeSpan.FromMinutes(5);
        using var store = NewStore(new DuckDBTaskStoreOptions { MaxTimeToLive = maxTimeToLive, CleanupInterval = Timeout.InfiniteTimeSpan });

        // Act
        var task = await CreateTask(store, timeToLive: TimeSpan.FromHours(1));

        // Assert
        await Assert.That(task.TimeToLive).IsEqualTo(maxTimeToLive);
    }

    [Test]
    public async ValueTask add_duckdb_mcp_task_store_registers_a_singleton() {
        // Arrange
        var services = new ServiceCollection().AddDuckDBMcpTaskStore(Db.ConnectionString);
        await using var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IMcpTaskStore>();
        var second = provider.GetRequiredService<IMcpTaskStore>();

        // Assert
        await Assert.That(first).IsSameReferenceAs(second);
    }
}
