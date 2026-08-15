using System.Text.Json;
using Kurrent.Surge.Mcp.DuckDB;
using Kurrent.Surge.Mcp.DuckDB.Experimental;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Extensions.Tasks;
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
    IMcpTaskStore NewStore(StoreFactory make) =>
        _store = make(Db.ConnectionString, Time);

    // DuckDB store with explicit options (TTL / sweep), on the fake clock so the sweep is deterministic.
    DuckDBMcpTaskStore NewStore(DuckDBMcpTaskStoreOptions options) {
        var store = new DuckDBMcpTaskStore(Db.ConnectionString, options, Time);
        _store = store;
        return store;
    }

    public void Dispose() =>
        (_store as IDisposable)?.Dispose();

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask create_task_returns_working_status_with_clock_timestamps(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store = NewStore(make);
        var now   = Time.GetUtcNow();

        // Act
        var task = await store.CreateTaskAsync(cancellationToken);

        // Assert
        await Assert.That(task.Status).IsEqualTo(McpTaskStatus.Working);
        await Assert.That(task.TaskId).IsNotNullOrEmpty();
        await Assert.That(task.CreatedAt).IsEqualTo(now);
        await Assert.That(task.LastUpdatedAt).IsEqualTo(now);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask get_task_returns_null_for_unknown_id(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store     = NewStore(make);
        var unknownId = Faker.Random.Guid().ToString();

        // Act
        var task = await store.GetTaskAsync(unknownId, cancellationToken);

        // Assert
        await Assert.That(task).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask get_task_returns_created_task(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store   = NewStore(make);
        var created = await store.CreateTaskAsync(cancellationToken);

        // Act
        var fetched = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.TaskId).IsEqualTo(created.TaskId);
        await Assert.That(fetched.Status).IsEqualTo(McpTaskStatus.Working);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_completed_transitions_to_completed_and_stores_result(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store           = NewStore(make);
        var created         = await store.CreateTaskAsync(cancellationToken);
        var result          = JsonSerializer.SerializeToElement(new ResultDto(Faker.Lorem.Sentence()));
        var expectedRawText = result.GetRawText();

        // Act
        await store.SetCompletedAsync(created.TaskId, result, cancellationToken);
        var fetched = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Completed);
        await Assert.That(fetched.Result).IsNotNull();
        await Assert.That(fetched.Result!.Value.GetRawText()).IsEqualTo(expectedRawText);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask completed_task_ignores_subsequent_set_failed_call(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store           = NewStore(make);
        var created         = await store.CreateTaskAsync(cancellationToken);
        var result          = JsonSerializer.SerializeToElement(new ResultDto(Faker.Lorem.Sentence()));
        var expectedRawText = result.GetRawText();
        
        await store.SetCompletedAsync(created.TaskId, result, cancellationToken);
        var error = JsonSerializer.SerializeToElement(new ErrorDto(Faker.Lorem.Sentence()));

        // Act — a terminal task must ignore a later transition attempt rather than throw.
        await store.SetFailedAsync(created.TaskId, error, cancellationToken);
        var fetched = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Completed);
        await Assert.That(fetched.Result!.Value.GetRawText()).IsEqualTo(expectedRawText);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_completed_on_missing_task_throws(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store           = NewStore(make);
        var missingId       = Faker.Random.Guid().ToString();
        var result          = JsonSerializer.SerializeToElement(new ResultDto(Faker.Lorem.Sentence()));
        var expectedMessage = $"Task '{missingId}' not found.";

        // Act / Assert
        await Assert
            .That(async () => await store.SetCompletedAsync(missingId, result, cancellationToken))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage(expectedMessage);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_failed_transitions_to_failed_and_stores_error(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store           = NewStore(make);
        var created         = await store.CreateTaskAsync(cancellationToken);
        var error           = JsonSerializer.SerializeToElement(new ErrorDto(Faker.Lorem.Sentence()));
        var expectedRawText = error.GetRawText();

        // Act
        await store.SetFailedAsync(created.TaskId, error, cancellationToken);
        var fetched = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Failed);
        await Assert.That(fetched.Error).IsNotNull();
        await Assert.That(fetched.Error!.Value.GetRawText()).IsEqualTo(expectedRawText);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_cancelled_returns_true_for_working_task_and_transitions_to_cancelled(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store   = NewStore(make);
        var created = await store.CreateTaskAsync(cancellationToken);

        // Act
        var cancelled = await store.SetCancelledAsync(created.TaskId, cancellationToken);
        var fetched   = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert
        await Assert.That(cancelled).IsTrue();
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Cancelled);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_cancelled_returns_false_for_missing_task(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store     = NewStore(make);
        var missingId = Faker.Random.Guid().ToString();

        // Act
        var cancelled = await store.SetCancelledAsync(missingId, cancellationToken);

        // Assert
        await Assert.That(cancelled).IsFalse();
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_cancelled_returns_false_for_already_terminal_task(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store   = NewStore(make);
        var created = await store.CreateTaskAsync(cancellationToken);
        var result  = JsonSerializer.SerializeToElement(new ResultDto(Faker.Lorem.Sentence()));
        await store.SetCompletedAsync(created.TaskId, result, cancellationToken);

        // Act
        var cancelled = await store.SetCancelledAsync(created.TaskId, cancellationToken);
        var fetched   = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert
        await Assert.That(cancelled).IsFalse();
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Completed);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_input_requests_transitions_to_input_required_and_exposes_pending_requests(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store         = NewStore(make);
        var created       = await store.CreateTaskAsync(cancellationToken);
        var requestKey    = Faker.Random.AlphaNumeric(8);
        var message       = Faker.Lorem.Sentence();
        var inputRequests = new Dictionary<string, InputRequest> { [requestKey] = InputRequest.ForElicitation(new() { Message = message }) };

        // Act
        await store.SetInputRequestsAsync(created.TaskId, inputRequests, cancellationToken);
        var fetched = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.InputRequired);
        await Assert.That(fetched.InputRequests).IsNotNull();
        await Assert.That(fetched.InputRequests!.ContainsKey(requestKey)).IsTrue();
        await Assert.That(fetched.InputRequests[requestKey].ElicitationParams?.Message).IsEqualTo(message);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask set_input_requests_on_missing_task_throws(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store           = NewStore(make);
        var missingId       = Faker.Random.Guid().ToString();
        var inputRequests   = new Dictionary<string, InputRequest> { [Faker.Random.AlphaNumeric(8)] = InputRequest.ForElicitation(new() { Message = Faker.Lorem.Sentence() }) };
        var expectedMessage = $"Task '{missingId}' not found.";

        // Act / Assert
        await Assert
            .That(async () => await store.SetInputRequestsAsync(missingId, inputRequests, cancellationToken))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage(expectedMessage);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask resolve_input_requests_removes_satisfied_request_and_returns_to_working(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store         = NewStore(make);
        var created       = await store.CreateTaskAsync(cancellationToken);
        var requestKey    = Faker.Random.AlphaNumeric(8);
        var inputRequests = new Dictionary<string, InputRequest> { [requestKey] = InputRequest.ForElicitation(new() { Message = Faker.Lorem.Sentence() }) };
        await store.SetInputRequestsAsync(created.TaskId, inputRequests, cancellationToken);
        var inputResponses = new Dictionary<string, InputResponse> { [requestKey] = InputResponse.FromElicitResult(new() { Action = "accept" }) };

        // Act
        await store.ResolveInputRequestsAsync(created.TaskId, inputResponses, cancellationToken);
        var fetched = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Working);
        await Assert.That(fetched.InputRequests).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask resolve_input_requests_raises_input_response_received_event(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store         = NewStore(make);
        var created       = await store.CreateTaskAsync(cancellationToken);
        var requestKey    = Faker.Random.AlphaNumeric(8);
        var inputRequests = new Dictionary<string, InputRequest> { [requestKey] = InputRequest.ForElicitation(new() { Message = Faker.Lorem.Sentence() }) };
        
        await store.SetInputRequestsAsync(created.TaskId, inputRequests, cancellationToken);
        
        var response       = InputResponse.FromElicitResult(new() { Action = "accept" });
        var inputResponses = new Dictionary<string, InputResponse> { [requestKey] = response };
        var received       = new List<InputResponseReceivedEventArgs>();
        
        store.InputResponseReceived += received.Add;

        // Act
        await store.ResolveInputRequestsAsync(created.TaskId, inputResponses, cancellationToken);

        // Assert
        await Assert.That(received.Count).IsEqualTo(1);
        await Assert.That(received[0].TaskId).IsEqualTo(created.TaskId);
        await Assert.That(received[0].RequestId).IsEqualTo(requestKey);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask resolve_input_requests_on_terminal_task_does_not_raise_event(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store   = NewStore(make);
        var created = await store.CreateTaskAsync(cancellationToken);
        var result  = JsonSerializer.SerializeToElement(new ResultDto(Faker.Lorem.Sentence()));
        
        await store.SetCompletedAsync(created.TaskId, result, cancellationToken);
        
        var inputResponses = new Dictionary<string, InputResponse> { [Faker.Random.AlphaNumeric(8)] = InputResponse.FromElicitResult(new() { Action = "accept" }) };
        var received       = new List<InputResponseReceivedEventArgs>();
        
        store.InputResponseReceived += received.Add;

        // Act — dropped silently: a terminal task has no listener left to act on the response.
        await store.ResolveInputRequestsAsync(created.TaskId, inputResponses, cancellationToken);

        // Assert
        await Assert.That(received.Count).IsEqualTo(0);
    }

    // DuckDB-specific: the throwing-subscriber isolation lives in DuckDBMcpTaskStore only. Promote this to the
    // shared Stores() fan-out once QuackMcpTaskStore gets the same isolation fix.
    [Test]
    public async ValueTask resolve_input_requests_survives_a_throwing_subscriber_and_still_notifies_the_rest(CancellationToken cancellationToken) {
        // Arrange — a live task with one pending request and two subscribers: the first throws, the second
        // records. The resolve is durably committed before the event fires, so a throwing subscriber must
        // neither fail the call (the caller would treat resolve as failed and retry, double-notifying) nor
        // stop later subscribers from running.
        var store      = new DuckDBMcpTaskStore(Db.ConnectionString, timeProvider: Time);
        _store         = store;
        var created    = await store.CreateTaskAsync(cancellationToken);
        var requestKey = Faker.Random.AlphaNumeric(8);

        await store.SetInputRequestsAsync(
            created.TaskId,
            new Dictionary<string, InputRequest> { [requestKey] = InputRequest.ForElicitation(new() { Message = Faker.Lorem.Sentence() }) },
            cancellationToken);

        var inputResponses     = new Dictionary<string, InputResponse> { [requestKey] = InputResponse.FromElicitResult(new() { Action = "accept" }) };
        var throwingHandlerRan = false;
        var received           = new List<InputResponseReceivedEventArgs>();

        // Subscription order is invocation order — the throwing handler runs BEFORE the recording one, so a
        // pass proves the recording handler was still reached despite the earlier throw.
        store.InputResponseReceived += _ => { throwingHandlerRan = true; throw new InvalidOperationException("subscriber boom"); };
        store.InputResponseReceived += received.Add;

        // Act — must complete without surfacing the subscriber's exception.
        await store.ResolveInputRequestsAsync(created.TaskId, inputResponses, cancellationToken);

        // Assert — the bad handler ran (so the test is not vacuous), and the good handler still received the
        // one response.
        await Assert.That(throwingHandlerRan).IsTrue();
        await Assert.That(received.Count).IsEqualTo(1);
        await Assert.That(received[0].TaskId).IsEqualTo(created.TaskId);
        await Assert.That(received[0].RequestId).IsEqualTo(requestKey);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask concurrent_writes_to_same_task_all_succeed_without_conflict(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        const int writers = 32;

        var store   = NewStore(make);
        var created = await store.CreateTaskAsync(cancellationToken);
        var keys    = Enumerable.Range(0, writers).Select(_ => Faker.Random.AlphaNumeric(10)).Distinct().ToArray();

        // Act — fire many writers concurrently at the SAME task row. Each merges one distinct
        // input-request key. Under DuckDB's optimistic MVCC these transactions collide on the
        // row; the store must absorb the write-write conflicts (DuckDBErrorType.Transaction) via
        // its bounded retry rather than surface them to the caller, and no merge may be lost.
        var tasks = keys
            .Select(key => Task.Run(() =>
                store.SetInputRequestsAsync(
                    created.TaskId, new Dictionary<string, InputRequest> { [key] = InputRequest.ForElicitation(new() { Message = Faker.Lorem.Sentence() }) }, cancellationToken),
                cancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert — no writer threw, the task is InputRequired, and every concurrent merge survived.
        var fetched = await store.GetTaskAsync(created.TaskId, cancellationToken);
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.InputRequired);
        await Assert.That(fetched.InputRequests).IsNotNull();
        await Assert.That(fetched.InputRequests!.Count).IsEqualTo(keys.Length);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask round_trips_result_json_with_quotes_and_special_characters(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange — a payload with single quotes, double quotes, backslashes, braces, and unicode.
        // This stresses SQL-string inlining/escaping in stores that forward statements as text (V3);
        // it is harmless for the parameterized stores. The raw JSON must survive the round-trip exactly.
        var store           = NewStore(make);
        var created         = await store.CreateTaskAsync(cancellationToken);
        var gnarly          = "it's a \"quoted\" value with 'apostrophes', a \\ backslash, {braces}, and unicode ☃ / 日本語";
        var result          = JsonSerializer.SerializeToElement(new ResultDto(gnarly));
        var expectedRawText = result.GetRawText();

        // Act
        await store.SetCompletedAsync(created.TaskId, result, cancellationToken);
        var fetched = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Completed);
        await Assert.That(fetched.Result).IsNotNull();
        await Assert.That(fetched.Result!.Value.GetRawText()).IsEqualTo(expectedRawText);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask resolve_input_requests_on_missing_task_throws(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange
        var store           = NewStore(make);
        var missingId       = Faker.Random.Guid().ToString();
        var inputResponses  = new Dictionary<string, InputResponse> { [Faker.Random.AlphaNumeric(8)] = InputResponse.FromElicitResult(new() { Action = "accept" }) };
        var expectedMessage = $"Task '{missingId}' not found.";

        // Act / Assert — resolving against an unknown id throws, symmetric with the other transition ops.
        await Assert
            .That(async () => await store.ResolveInputRequestsAsync(missingId, inputResponses, cancellationToken))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessage(expectedMessage);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask failed_task_ignores_subsequent_transition(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange — a task driven to the failed terminal state.
        var store        = NewStore(make);
        var created      = await store.CreateTaskAsync(cancellationToken);
        var error        = JsonSerializer.SerializeToElement(new ErrorDto(Faker.Lorem.Sentence()));
        var expectedError = error.GetRawText();
        await store.SetFailedAsync(created.TaskId, error, cancellationToken);

        // Act — a later completion attempt on a terminal task is ignored, neither applied nor thrown.
        var result = JsonSerializer.SerializeToElement(new ResultDto(Faker.Lorem.Sentence()));
        await store.SetCompletedAsync(created.TaskId, result, cancellationToken);
        var fetched = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert — still failed, original error intact.
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Failed);
        await Assert.That(fetched.Error!.Value.GetRawText()).IsEqualTo(expectedError);
    }

    [Test]
    [MethodDataSource(nameof(Stores))]
    public async ValueTask cancelled_task_ignores_subsequent_transition(StoreFactory make, CancellationToken cancellationToken) {
        // Arrange — a task driven to the cancelled terminal state.
        var store   = NewStore(make);
        var created = await store.CreateTaskAsync(cancellationToken);
        await store.SetCancelledAsync(created.TaskId, cancellationToken);

        // Act — a later completion attempt on a terminal task is ignored, neither applied nor thrown.
        var result = JsonSerializer.SerializeToElement(new ResultDto(Faker.Lorem.Sentence()));
        await store.SetCompletedAsync(created.TaskId, result, cancellationToken);
        var fetched = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert — still cancelled, no result stored.
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Cancelled);
        await Assert.That(fetched.Result).IsNull();
    }

    // DuckDB-specific: the TTL sweep lives in DuckDBMcpTaskStore only (the experimental Quack store has no
    // sweep), so these run outside the shared Stores() fan-out. FakeTimeProvider fires the store's sweep
    // timer synchronously as the clock advances, so each Advance triggers the sweep deterministically.
    [Test]
    public async ValueTask expired_live_task_is_marked_failed_within_the_grace_window(CancellationToken cancellationToken) {
        // Arrange — TTL elapses one sweep tick out, with a grace window equal to the interval.
        var options = new DuckDBMcpTaskStoreOptions {
            DefaultTimeToLive = TimeSpan.FromSeconds(60),
            SweepInterval     = TimeSpan.FromSeconds(60),
            GracePeriod       = TimeSpan.FromSeconds(60)
        };
        var store   = NewStore(options);
        var created = await store.CreateTaskAsync(cancellationToken);

        // Act — the tick at +60s lands exactly on expiry, still inside the grace window.
        Time.Advance(TimeSpan.FromSeconds(60));
        var fetched = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert — the live task was flipped to failed and carries the expiry error, not yet deleted.
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Failed);
        await Assert.That(fetched.Error).IsNotNull();
        await Assert.That(fetched.Error!.Value.GetProperty("code").GetInt32()).IsEqualTo(-32001);
    }

    [Test]
    public async ValueTask expired_task_is_deleted_after_the_grace_period(CancellationToken cancellationToken) {
        // Arrange
        var options = new DuckDBMcpTaskStoreOptions {
            DefaultTimeToLive = TimeSpan.FromSeconds(60),
            SweepInterval     = TimeSpan.FromSeconds(60),
            GracePeriod       = TimeSpan.FromSeconds(60)
        };
        var store   = NewStore(options);
        var created = await store.CreateTaskAsync(cancellationToken);

        // Act — tick at +60s marks it failed; tick at +120s (past TTL + grace) deletes the row.
        Time.Advance(TimeSpan.FromSeconds(60));
        Time.Advance(TimeSpan.FromSeconds(60));
        var fetched = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert — purged; a tasks/get now resolves to not-found (SEP-2663 compliant).
        await Assert.That(fetched).IsNull();
    }

    [Test]
    public async ValueTask task_is_not_swept_before_its_ttl_elapses(CancellationToken cancellationToken) {
        // Arrange — a 10-minute TTL, so ticks that fire before it elapses must leave the task untouched.
        var options = new DuckDBMcpTaskStoreOptions {
            DefaultTimeToLive = TimeSpan.FromMinutes(10),
            SweepInterval     = TimeSpan.FromMinutes(1),
            GracePeriod       = TimeSpan.FromMinutes(1)
        };
        var store   = NewStore(options);
        var created = await store.CreateTaskAsync(cancellationToken);

        // Act — advance two minutes: two sweep ticks fire, both well before the TTL elapses.
        Time.Advance(TimeSpan.FromMinutes(2));
        var fetched = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert — the sweep did not touch a task whose TTL has not yet elapsed.
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskStatus.Working);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-60)]
    public async ValueTask non_positive_sweep_interval_is_rejected(int intervalSeconds) {
        // Arrange — the sweep is mandatory, so a zero or negative interval must be refused at construction,
        // not silently accepted as an off switch.
        var options = new DuckDBMcpTaskStoreOptions { SweepInterval = TimeSpan.FromSeconds(intervalSeconds) };

        // Act / Assert
        await Assert
            .That(() => new DuckDBMcpTaskStore(Db.ConnectionString, options, Time))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-60)]
    public async ValueTask non_positive_default_time_to_live_is_rejected(int ttlSeconds) {
        // Arrange — every task must have a bounded lifetime, so a zero or negative default TTL must be
        // refused at construction (a task born already expired would be failed by the next sweep).
        var options = new DuckDBMcpTaskStoreOptions { DefaultTimeToLive = TimeSpan.FromSeconds(ttlSeconds) };

        // Act / Assert
        await Assert
            .That(() => new DuckDBMcpTaskStore(Db.ConnectionString, options, Time))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async ValueTask resolve_input_requests_ignores_a_response_for_a_key_that_was_never_outstanding(CancellationToken cancellationToken) {
        // Arrange — one genuine outstanding request, plus a subscriber recording every notification.
        var store          = NewStore(new DuckDBMcpTaskStoreOptions());
        var created        = await store.CreateTaskAsync(cancellationToken);
        var outstandingKey = Faker.Random.AlphaNumeric(8);
        var ghostKey       = Faker.Random.AlphaNumeric(9);

        await store.SetInputRequestsAsync(
            created.TaskId,
            new Dictionary<string, InputRequest> { [outstandingKey] = InputRequest.ForElicitation(new() { Message = Faker.Lorem.Sentence() }) },
            cancellationToken);

        var received = new List<InputResponseReceivedEventArgs>();
        store.InputResponseReceived += received.Add;

        // Act — respond to the outstanding key AND a key that was never requested on this task.
        var response = InputResponse.FromElicitResult(new() { Action = "accept" });
        await store.ResolveInputRequestsAsync(
            created.TaskId,
            new Dictionary<string, InputResponse> { [outstandingKey] = response, [ghostKey] = response },
            cancellationToken);

        // Assert — exactly one event, for the outstanding key only; the ghost key is ignored (SEP-2663).
        await Assert.That(received.Count).IsEqualTo(1);
        await Assert.That(received[0].RequestId).IsEqualTo(outstandingKey);
    }

    [Test]
    public async ValueTask expired_task_survives_until_exactly_ttl_plus_grace(CancellationToken cancellationToken) {
        // Arrange — TTL 60s, grace 120s: the row must survive until exactly createdAt + 60s + 120s = +180s,
        // then be deleted on the tick at that instant (delete predicate is inclusive: expires_at <= now - grace).
        var options = new DuckDBMcpTaskStoreOptions {
            DefaultTimeToLive = TimeSpan.FromSeconds(60),
            SweepInterval     = TimeSpan.FromSeconds(60),
            GracePeriod       = TimeSpan.FromSeconds(120)
        };
        var store   = NewStore(options);
        var created = await store.CreateTaskAsync(cancellationToken);

        // Act 1 — advance to +120s (past TTL, still inside grace): failed but still present.
        Time.Advance(TimeSpan.FromSeconds(120));
        var duringGrace = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Act 2 — advance to exactly +180s (TTL + grace): deleted on this tick.
        Time.Advance(TimeSpan.FromSeconds(60));
        var afterGrace = await store.GetTaskAsync(created.TaskId, cancellationToken);

        // Assert
        await Assert.That(duringGrace).IsNotNull();
        await Assert.That(duringGrace!.Status).IsEqualTo(McpTaskStatus.Failed);
        await Assert.That(afterGrace).IsNull();
    }

    [Test]
    public async ValueTask add_duckdb_mcp_task_store_registers_singleton(CancellationToken cancellationToken) {
        // Arrange
        var services = new ServiceCollection().AddDuckDBMcpTaskStore(Db.ConnectionString);

        await using var provider = services.BuildServiceProvider();

        // Act
        var first  = provider.GetRequiredService<IMcpTaskStore>();
        var second = provider.GetRequiredService<IMcpTaskStore>();

        // Assert
        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsSameReferenceAs(first);
    }

    [Test]
    public async ValueTask add_duckdb_mcp_task_store_binds_options_from_configuration(CancellationToken cancellationToken) {
        // Arrange — a configuration section for the store with a non-default poll interval.
        var expectedPollIntervalMs = Faker.Random.Long(2_000, 9_000);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> {
                    [$"{DuckDBMcpTaskStoreOptions.ConfigurationSection}:{nameof(DuckDBMcpTaskStoreOptions.DefaultPollIntervalMs)}"] = expectedPollIntervalMs.ToString()
                })
            .Build();

        await using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddDuckDBMcpTaskStore(Db.ConnectionString)
            .BuildServiceProvider();

        var store = provider.GetRequiredService<IMcpTaskStore>();

        // Act — the poll interval a created task advertises comes from the bound options.
        var task = await store.CreateTaskAsync(cancellationToken);

        // Assert
        await Assert.That(task.PollIntervalMs).IsEqualTo(expectedPollIntervalMs);
    }

    [Test]
    public async ValueTask add_duckdb_mcp_task_store_binds_timespan_options_from_configuration(CancellationToken cancellationToken) {
        // Arrange — a TimeSpan-typed option bound from configuration. Guards the known TimeSpan/config-binding
        // quirk in this codebase; the existing binding test only covers a long-typed option.
        var expectedTtl = TimeSpan.FromHours(2);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> {
                    [$"{DuckDBMcpTaskStoreOptions.ConfigurationSection}:{nameof(DuckDBMcpTaskStoreOptions.DefaultTimeToLive)}"] = expectedTtl.ToString()
                })
            .Build();

        await using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddDuckDBMcpTaskStore(Db.ConnectionString)
            .BuildServiceProvider();

        var store = provider.GetRequiredService<IMcpTaskStore>();

        // Act — the TTL a created task advertises comes from the bound TimeSpan option.
        var task = await store.CreateTaskAsync(cancellationToken);

        // Assert
        await Assert.That(task.TimeToLive).IsEqualTo(expectedTtl);
    }

    sealed record ResultDto(string Value);

    sealed record ErrorDto(string Message);
}