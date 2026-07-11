using Microsoft.Extensions.Time.Testing;

namespace Kurrent.Surge.Mcp.Core.Tests;

[Category("Mcp")]
[Category("DuckDB")]
[Timeout(30_000)]
public class DuckDBTaskStoreTests {
    [ClassDataSource<TempDuckDb>(Shared = SharedType.None)]
    public required TempDuckDb Db { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    // Deterministic clock so TTL/expiry/timestamps are exact and reproducible.
    FakeTimeProvider Time { get; } = new();

    // Cleanup timer disabled by default — expiry is exercised via the live read-path filter;
    // one dedicated test re-enables it to cover the background sweep.
    DuckDBTaskStore NewStore(DuckDBTaskStoreOptions? options = null) =>
        new(Db.ConnectionString, options ?? new DuckDBTaskStoreOptions { CleanupInterval = Timeout.InfiniteTimeSpan }, Time);

    [Test]
    public async ValueTask create_stores_task_in_working_state() {
        // Arrange
        using var store = NewStore();
        var now = Time.GetUtcNow();

        // Act
        var task = await store.CreateAsync(new CreateTaskInput());

        // Assert
        await Assert.That(task.Status).IsEqualTo(McpTaskState.Working);
        await Assert.That(task.TaskId).IsNotNullOrEmpty();
        await Assert.That(task.CreatedAt).IsEqualTo(now);
        await Assert.That(task.LastUpdatedAt).IsEqualTo(now);
        await Assert.That(task.PollInterval).IsEqualTo(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async ValueTask create_generates_distinct_ids() {
        // Arrange
        using var store = NewStore();

        // Act
        var first  = await store.CreateAsync(new CreateTaskInput());
        var second = await store.CreateAsync(new CreateTaskInput());

        // Assert
        await Assert.That(first.TaskId).IsNotEqualTo(second.TaskId);
    }

    [Test]
    public async ValueTask get_returns_null_for_unknown_task() {
        // Arrange
        using var store = NewStore();

        // Act
        var task = await store.GetAsync(Faker.Random.Guid().ToString());

        // Assert
        await Assert.That(task).IsNull();
    }

    [Test]
    public async ValueTask get_returns_created_task() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateAsync(new CreateTaskInput { SessionId = Faker.Random.AlphaNumeric(8) });

        // Act
        var fetched = await store.GetAsync(created.TaskId);

        // Assert
        await Assert.That(fetched).IsEqualTo(created);
    }

    [Test]
    public async ValueTask update_transforms_and_persists() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateAsync(new CreateTaskInput());
        var message = Faker.Lorem.Sentence();

        // Act
        var updated = await store.UpdateAsync(created.TaskId, task => task with {
            Status        = McpTaskState.Completed,
            StatusMessage = message,
            ResultJson    = """{"ok":true}"""
        });
        var fetched = await store.GetAsync(created.TaskId);

        // Assert
        await Assert.That(updated).IsNotNull();
        await Assert.That(updated!.Status).IsEqualTo(McpTaskState.Completed);
        await Assert.That(updated.StatusMessage).IsEqualTo(message);
        await Assert.That(fetched).IsEqualTo(updated);
    }

    [Test]
    public async ValueTask update_bumps_last_updated_at() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateAsync(new CreateTaskInput());
        Time.Advance(TimeSpan.FromMinutes(5));
        var expectedLastUpdated = Time.GetUtcNow();

        // Act
        var updated = await store.UpdateAsync(created.TaskId, task => task with { Status = McpTaskState.Completed });

        // Assert
        await Assert.That(updated!.LastUpdatedAt).IsEqualTo(expectedLastUpdated);
        await Assert.That(updated.CreatedAt).IsEqualTo(created.CreatedAt);
    }

    [Test]
    public async ValueTask update_noop_transform_does_not_bump_last_updated_at() {
        // Arrange — an unchanged transform is how idempotent no-ops (e.g. cancel-on-terminal) are expressed.
        using var store = NewStore();
        var created = await store.CreateAsync(new CreateTaskInput());
        Time.Advance(TimeSpan.FromMinutes(5));

        // Act
        var result = await store.UpdateAsync(created.TaskId, task => task);

        // Assert
        await Assert.That(result).IsEqualTo(created);
        await Assert.That(result!.LastUpdatedAt).IsEqualTo(created.LastUpdatedAt);
    }

    [Test]
    public async ValueTask update_returns_null_for_unknown_task() {
        // Arrange
        using var store = NewStore();

        // Act
        var result = await store.UpdateAsync(Faker.Random.Guid().ToString(), task => task with { Status = McpTaskState.Failed });

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async ValueTask update_preserves_id_and_created_at_against_a_misbehaving_transform() {
        // Arrange
        using var store = NewStore();
        var created = await store.CreateAsync(new CreateTaskInput());

        // Act — transform tries to change identity fields; the store must ignore that.
        var updated = await store.UpdateAsync(created.TaskId, task => task with {
            TaskId    = "hijacked",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Status    = McpTaskState.Failed
        });

        // Assert
        await Assert.That(updated!.TaskId).IsEqualTo(created.TaskId);
        await Assert.That(updated.CreatedAt).IsEqualTo(created.CreatedAt);
        await Assert.That(updated.Status).IsEqualTo(McpTaskState.Failed);
    }

    [Test]
    public async ValueTask status_persists_by_name_and_round_trips() {
        // Arrange — Failed/Cancelled have different ordinals across SDK versions; name persistence guards it.
        using var store = NewStore();
        var created = await store.CreateAsync(new CreateTaskInput());

        // Act
        await store.UpdateAsync(created.TaskId, task => task with { Status = McpTaskState.Cancelled });
        var fetched = await store.GetAsync(created.TaskId);

        // Assert
        await Assert.That(fetched!.Status).IsEqualTo(McpTaskState.Cancelled);
    }

    [Test]
    public async ValueTask list_orders_by_creation_time() {
        // Arrange — advance the clock between creates so ordering is deterministic.
        using var store = NewStore();
        var expected = new List<string>();
        for (var i = 0; i < 3; i++) {
            var task = await store.CreateAsync(new CreateTaskInput());
            expected.Add(task.TaskId);
            Time.Advance(TimeSpan.FromSeconds(1));
        }

        // Act
        var page = await store.ListAsync();
        var actual = page.Tasks.Select(t => t.TaskId).ToList();

        // Assert
        await Assert.That(actual).IsEquivalentTo(expected);
        await Assert.That(page.NextCursor).IsNull();
    }

    [Test]
    public async ValueTask list_paginates_without_duplicates_or_gaps() {
        // Arrange
        using var store = NewStore(new DuckDBTaskStoreOptions { PageSize = 2, CleanupInterval = Timeout.InfiniteTimeSpan });
        var created = new HashSet<string>();
        for (var i = 0; i < 5; i++) {
            var task = await store.CreateAsync(new CreateTaskInput());
            created.Add(task.TaskId);
            Time.Advance(TimeSpan.FromSeconds(1));
        }

        // Act — walk every page following the cursor.
        var seen = new List<string>();
        string? cursor = null;
        do {
            var page = await store.ListAsync(cursor: cursor);
            seen.AddRange(page.Tasks.Select(t => t.TaskId));
            cursor = page.NextCursor;
        } while (cursor is not null);

        // Assert — every task seen exactly once.
        await Assert.That(seen.Count).IsEqualTo(5);
        await Assert.That(seen.ToHashSet()).IsEquivalentTo(created);
    }

    [Test]
    public async ValueTask list_filters_by_session() {
        // Arrange
        using var store = NewStore();
        var mine    = await store.CreateAsync(new CreateTaskInput { SessionId = "session-a" });
        await store.CreateAsync(new CreateTaskInput { SessionId = "session-b" });

        // Act
        var page = await store.ListAsync(sessionId: "session-a");

        // Assert
        await Assert.That(page.Tasks.Count).IsEqualTo(1);
        await Assert.That(page.Tasks[0].TaskId).IsEqualTo(mine.TaskId);
    }

    [Test]
    public async ValueTask list_excludes_expired_tasks() {
        // Arrange
        using var store = NewStore();
        await store.CreateAsync(new CreateTaskInput { RequestedTimeToLive = TimeSpan.FromSeconds(30) });

        // Act
        Time.Advance(TimeSpan.FromMinutes(1));
        var page = await store.ListAsync();

        // Assert
        await Assert.That(page.Tasks.Count).IsEqualTo(0);
    }

    [Test]
    public async ValueTask get_returns_expired_task_before_sweep() {
        // Arrange — direct get by id does NOT filter expiry (matches the reference direct-get path).
        using var store = NewStore();
        var created = await store.CreateAsync(new CreateTaskInput { RequestedTimeToLive = TimeSpan.FromSeconds(30) });

        // Act
        Time.Advance(TimeSpan.FromMinutes(1));
        var fetched = await store.GetAsync(created.TaskId);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.TaskId).IsEqualTo(created.TaskId);
    }

    [Test]
    public async ValueTask create_defaults_ttl_from_options() {
        // Arrange
        var ttl = TimeSpan.FromHours(1);
        using var store = NewStore(new DuckDBTaskStoreOptions { DefaultTimeToLive = ttl, CleanupInterval = Timeout.InfiniteTimeSpan });
        var now = Time.GetUtcNow();

        // Act
        var task = await store.CreateAsync(new CreateTaskInput());

        // Assert
        await Assert.That(task.TimeToLive).IsEqualTo(ttl);
        await Assert.That(task.ExpiresAt).IsEqualTo(now + ttl);
    }

    [Test]
    public async ValueTask create_clamps_requested_ttl_to_max() {
        // Arrange
        var max = TimeSpan.FromMinutes(5);
        using var store = NewStore(new DuckDBTaskStoreOptions { MaxTimeToLive = max, CleanupInterval = Timeout.InfiniteTimeSpan });

        // Act
        var task = await store.CreateAsync(new CreateTaskInput { RequestedTimeToLive = TimeSpan.FromHours(1) });

        // Assert
        await Assert.That(task.TimeToLive).IsEqualTo(max);
    }

    [Test]
    public async ValueTask create_enforces_global_max_tasks_including_expired() {
        // Arrange — the global cap counts expired-but-unswept tasks too.
        using var store = NewStore(new DuckDBTaskStoreOptions { MaxTasks = 2, CleanupInterval = Timeout.InfiniteTimeSpan });
        await store.CreateAsync(new CreateTaskInput { RequestedTimeToLive = TimeSpan.FromSeconds(30) });
        await store.CreateAsync(new CreateTaskInput { RequestedTimeToLive = TimeSpan.FromSeconds(30) });
        Time.Advance(TimeSpan.FromMinutes(1)); // both now expired, but not swept

        // Act / Assert
        await Assert.That(async () => await store.CreateAsync(new CreateTaskInput()))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async ValueTask create_per_session_limit_excludes_expired() {
        // Arrange
        using var store = NewStore(new DuckDBTaskStoreOptions { MaxTasksPerSession = 1, CleanupInterval = Timeout.InfiniteTimeSpan });
        await store.CreateAsync(new CreateTaskInput { SessionId = "s", RequestedTimeToLive = TimeSpan.FromSeconds(30) });
        Time.Advance(TimeSpan.FromMinutes(1)); // the first task expired → no longer counts

        // Act — a second create in the same session now succeeds.
        var second = await store.CreateAsync(new CreateTaskInput { SessionId = "s" });

        // Assert
        await Assert.That(second.SessionId).IsEqualTo("s");
    }

    [Test]
    public async ValueTask create_per_session_limit_ignores_null_session() {
        // Arrange
        using var store = NewStore(new DuckDBTaskStoreOptions { MaxTasksPerSession = 1, CleanupInterval = Timeout.InfiniteTimeSpan });

        // Act
        await store.CreateAsync(new CreateTaskInput());
        var second = await store.CreateAsync(new CreateTaskInput());

        // Assert — the anonymous session is never capped by the per-session limit.
        await Assert.That(second.SessionId).IsNull();
    }

    [Test]
    public void options_reject_default_ttl_exceeding_max() {
        // Arrange
        var options = new DuckDBTaskStoreOptions { DefaultTimeToLive = TimeSpan.FromHours(2), MaxTimeToLive = TimeSpan.FromHours(1) };

        // Act / Assert
        Assert.Throws<ArgumentException>(() => _ = new DuckDBTaskStore(Db.ConnectionString, options, Time));
    }

    [Test]
    public void options_reject_nonpositive_page_size() {
        // Arrange
        var options = new DuckDBTaskStoreOptions { PageSize = 0 };

        // Act / Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new DuckDBTaskStore(Db.ConnectionString, options, Time));
    }

    [Test]
    public async ValueTask cleanup_sweep_deletes_expired_tasks() {
        // Arrange — enable the timer; expiry sweep runs when the fake clock advances past the interval.
        using var store = NewStore(new DuckDBTaskStoreOptions { CleanupInterval = TimeSpan.FromMinutes(1) });
        var created = await store.CreateAsync(new CreateTaskInput { RequestedTimeToLive = TimeSpan.FromSeconds(30) });

        // Act
        Time.Advance(TimeSpan.FromMinutes(2)); // fires the cleanup timer, which deletes the expired row
        var fetched = await store.GetAsync(created.TaskId);

        // Assert — the row is physically gone, so even the expiry-agnostic direct get returns null.
        await Assert.That(fetched).IsNull();
    }

    [Test]
    public async ValueTask tasks_survive_store_reopen() {
        // Arrange — persist a task, then close the store (releasing the file).
        string taskId;
        using (var store = NewStore()) {
            var created = await store.CreateAsync(new CreateTaskInput { SessionId = "durable" });
            taskId = created.TaskId;
        }

        // Act — a fresh store over the same file must still find it.
        using var reopened = NewStore();
        var fetched = await reopened.GetAsync(taskId);

        // Assert
        await Assert.That(fetched).IsNotNull();
        await Assert.That(fetched!.TaskId).IsEqualTo(taskId);
        await Assert.That(fetched.SessionId).IsEqualTo("durable");
    }
}
