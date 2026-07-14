using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kurrent.Surge.Mcp.Tests;

/// <summary>
/// Conformance tests for every <see cref="IMcpTaskStore"/> (ModelContextProtocol.Core 2.0)
/// implementation. Each test runs once per store in <see cref="Stores"/>, so the whole suite is
/// implementation-agnostic — add or remove a store with a single line in that list. Mirrors the
/// behavior of the SDK's own reference <c>InMemoryMcpTaskStore</c>.
/// </summary>
[Category("Mcp")]
[Timeout(30_000)]
public sealed class McpTaskStoreConformanceTests : IDisposable {
    [ClassDataSource<TempDuckDb>(Shared = SharedType.None)]
    public required TempDuckDb Db { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    // Deterministic clock so CreatedAt/LastUpdatedAt assertions are exact and reproducible.
    FakeTimeProvider Time { get; } = new();

    IMcpTaskStore? _store;

    /// <summary>Builds an <see cref="IMcpTaskStore"/> from a connection string and clock.</summary>
    public delegate IMcpTaskStore StoreFactory(string connectionString, TimeProvider timeProvider);

    // The implementations under test. Add or remove a store here and the entire suite follows. Each is
    // built with its default resilience (the write-conflict retry) — i.e. how it is actually used — so the
    // concurrency test verifies that same-task writers converge under contention.
    public static IEnumerable<Func<StoreFactory>> Stores() {
        yield return () => (cs, time) => new DuckDBMcpTaskStore(cs, timeProvider: time);
        yield return () => (cs, time) => new QuackMcpTaskStore(cs, timeProvider: time);
    }

    // One store per test, disposed with the test instance.
    IMcpTaskStore NewStore(StoreFactory make) => _store = make(Db.ConnectionString, Time);

    public void Dispose() => (_store as IDisposable)?.Dispose();

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask create_task_returns_working_status_with_clock_timestamps(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
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
    [MethodDataSource(nameof(Stores))]
    public async ValueTask get_task_returns_null_for_unknown_id(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
        var unknownId = Faker.Random.Guid().ToString();

        // Act
        var task = await store.GetTaskAsync(unknownId);

        // Assert
        await Assert.That(task).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask get_task_returns_created_task(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
        var created = await store.CreateTaskAsync();

        // Act
        var fetched = await store.GetTaskAsync(created.TaskId);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.TaskId).IsEqualTo(created.TaskId);
        await Assert.That(fetched.Status).IsEqualTo(McpTaskStatus.Working);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_completed_transitions_to_completed_and_stores_result(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
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
    [MethodDataSource(nameof(Stores))]
    public async ValueTask completed_task_ignores_subsequent_set_failed_call(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
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
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_completed_on_missing_task_throws(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
        var missingId = Faker.Random.Guid().ToString();
        var result = JsonSerializer.SerializeToElement(new ResultDto(Faker.Lorem.Sentence()));
        var expectedMessage = $"Task '{missingId}' not found.";

        // Act / Assert
        await Assert.That(async () => await store.SetCompletedAsync(missingId, result))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage(expectedMessage);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_failed_transitions_to_failed_and_stores_error(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
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
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_cancelled_returns_true_for_working_task_and_transitions_to_cancelled(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
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
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_cancelled_returns_false_for_missing_task(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
        var missingId = Faker.Random.Guid().ToString();

        // Act
        var cancelled = await store.SetCancelledAsync(missingId);

        // Assert
        await Assert.That(cancelled).IsFalse();
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_cancelled_returns_false_for_already_terminal_task(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
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
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_input_requests_transitions_to_input_required_and_exposes_pending_requests(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
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
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_input_requests_on_missing_task_throws(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
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
    [MethodDataSource(nameof(Stores))]
    public async ValueTask resolve_input_requests_removes_satisfied_request_and_returns_to_working(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
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
    [MethodDataSource(nameof(Stores))]
    public async ValueTask resolve_input_requests_raises_input_response_received_event(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
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
    [MethodDataSource(nameof(Stores))]
    public async ValueTask resolve_input_requests_on_terminal_task_does_not_raise_event(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
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
    [MethodDataSource(nameof(Stores))]
    public async ValueTask concurrent_writes_to_same_task_all_succeed_without_conflict(StoreFactory make) {
        // Arrange
        var store = NewStore(make);
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
    [MethodDataSource(nameof(Stores))]
    public async ValueTask round_trips_result_json_with_quotes_and_special_characters(StoreFactory make) {
        // Arrange — a payload with single quotes, double quotes, backslashes, braces, and unicode.
        // This stresses SQL-string inlining/escaping in stores that forward statements as text (V3);
        // it is harmless for the parameterized stores. The raw JSON must survive the round-trip exactly.
        var store = NewStore(make);
        var created = await store.CreateTaskAsync();
        var gnarly = "it's a \"quoted\" value with 'apostrophes', a \\ backslash, {braces}, and unicode ☃ / 日本語";
        var result = JsonSerializer.SerializeToElement(new ResultDto(gnarly));
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

    [Test]
    public async ValueTask add_duckdb_mcp_task_store_binds_options_from_configuration() {
        // Arrange — a configuration section for the store with a non-default poll interval.
        var expectedPollIntervalMs = Faker.Random.Long(2_000, 9_000);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                [$"{DuckDBTaskStoreOptions.ConfigurationSection}:{nameof(DuckDBTaskStoreOptions.DefaultPollIntervalMs)}"] = expectedPollIntervalMs.ToString()
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddDuckDBMcpTaskStore(Db.ConnectionString)
            .BuildServiceProvider();

        var store = provider.GetRequiredService<IMcpTaskStore>();

        // Act — the poll interval a created task advertises comes from the bound options.
        var task = await store.CreateTaskAsync();

        // Assert
        await Assert.That(task.PollIntervalMs).IsEqualTo(expectedPollIntervalMs);
    }

    sealed record ResultDto(string Value);

    sealed record ErrorDto(string Message);
}
