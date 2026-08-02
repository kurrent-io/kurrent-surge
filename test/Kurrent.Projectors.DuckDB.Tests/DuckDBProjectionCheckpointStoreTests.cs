using Kurrent.Quack;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Models;
using Kurrent.Testkit.TUnit.Assertions;

namespace Kurrent.Projectors.DuckDB.Tests;

/// <summary>
/// Covers <see cref="DuckDBProjectionCheckpointStore.Reset"/>, whose failure modes are all silent: a
/// partition left without a row resumes from the configured initial position instead of the override,
/// and a partition left with a stale row resumes ahead of it. Every assertion goes through
/// <see cref="DuckDBProjectionCheckpointStore.Load"/>, because that is what the drone consumes — the
/// stored rows only matter through it.
/// </summary>
[Category("Projections")]
[Category("DuckDB")]
[Category("Integration")]
[Timeout(30_000)]
public class DuckDBProjectionCheckpointStoreTests {
    [ClassDataSource<DuckDBTestFixture>(Shared = SharedType.None)]
    public required DuckDBTestFixture DuckDB { get; [UsedImplicitly] init; }

    static readonly LogPosition Rewound = LogPosition.From(100);
    static readonly LogPosition Ahead   = LogPosition.From(900);
    static readonly LogPosition Moved   = LogPosition.From(1500);

    [Test]
    public async ValueTask reset_rewinds_every_owned_partition_to_the_start_position(CancellationToken ct) {
        // Arrange
        var store = NewStore();
        var owned = Partitions(0, 1);

        await using var client = DuckDB.GetConnection();

        await store.EnsureStoreExists(client, ct);
        await Seed(store, client, owned, Ahead, ct);

        var expected = owned.Select(partition => RecordPosition.From(Rewound, partition)).ToArray();

        // Act
        await store.Reset(client, Rewound, owned, ct);

        // Assert
        var resumeFrom = await store.Load(client, owned, SubscriptionInitialPosition.Earliest, ct);

        await Assert.That(resumeFrom).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask reset_creates_a_row_for_a_partition_that_had_none(CancellationToken ct) {
        // Arrange — partition 1 has never been written, so there is nothing for a rewind to update.
        // Without a row it falls back to the initial position and the override is silently discarded.
        var store = NewStore();
        var owned = Partitions(0, 1);

        await using var client = DuckDB.GetConnection();

        await store.EnsureStoreExists(client, ct);
        await Seed(store, client, Partitions(0), Ahead, ct);

        var expected = owned.Select(partition => RecordPosition.From(Rewound, partition)).ToArray();

        // Act
        await store.Reset(client, Rewound, owned, ct);

        // Assert
        var resumeFrom = await store.Load(client, owned, SubscriptionInitialPosition.Earliest, ct);

        await Assert.That(resumeFrom).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask reset_prunes_partitions_no_longer_owned(CancellationToken ct) {
        // Arrange — a run that shrinks its partition count and later grows it back would otherwise
        // resume the dropped partitions from where they were before the rewind, skipping everything
        // in between.
        var store    = NewStore();
        var original = Partitions(0, 1, 2, 3);
        var owned    = Partitions(0, 1);

        await using var client = DuckDB.GetConnection();

        await store.EnsureStoreExists(client, ct);
        await Seed(store, client, original, Ahead, ct);

        var expected = new[] {
            RecordPosition.From(Rewound, Partition.From(0)),
            RecordPosition.From(Rewound, Partition.From(1)),
            RecordPosition.Earliest with { Partition = Partition.From(2) },
            RecordPosition.Earliest with { Partition = Partition.From(3) }
        };

        // Act
        await store.Reset(client, Rewound, owned, ct);

        // Assert — reading back the ORIGINAL partition set: the two dropped rows must be gone, so
        // they resolve to the fallback rather than to the position they held before the rewind.
        var resumeFrom = await store.Load(client, original, SubscriptionInitialPosition.Earliest, ct);

        await Assert.That(resumeFrom).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask reset_leaves_another_processors_checkpoints_untouched(CancellationToken ct) {
        // Arrange — two projections share one database, which is the whole point of co-locating
        // checkpoints with projected state. A reset is scoped to its own processor or it destroys
        // the other's resume position with no error.
        var rewinding = NewStore();
        var bystander = NewStore();
        var owned     = Partitions(0, 1);

        await using var client = DuckDB.GetConnection();

        await rewinding.EnsureStoreExists(client, ct);
        await Seed(rewinding, client, owned, Ahead, ct);
        await Seed(bystander, client, owned, Ahead, ct);

        var expected = owned.Select(partition => RecordPosition.From(Ahead, partition)).ToArray();

        // Act
        await rewinding.Reset(client, Rewound, owned, ct);

        // Assert
        var resumeFrom = await bystander.Load(client, owned, SubscriptionInitialPosition.Earliest, ct);

        await Assert.That(resumeFrom).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask store_rejects_the_batch_when_a_partition_moved_underneath_the_projection(
        CancellationToken ct
    ) {
        // Arrange — both partitions are stored at Ahead. The batch claims partition 1 was last left at
        // Rewound, which is the shape of a second writer having advanced it in between.
        var store = NewStore();
        var owned = Partitions(0, 1);

        await using var client = DuckDB.GetConnection();

        await store.EnsureStoreExists(client, ct);
        await Seed(store, client, owned, Ahead, ct);

        var expected = new[] {
            RecordPosition.From(Ahead, Partition.From(0)),     // matches what is stored
            RecordPosition.From(Rewound, Partition.From(1))    // does not — the guard must reject it
        };

        var next = owned.Select(partition => RecordPosition.From(Moved, partition)).ToArray();

        // Act / Assert — the whole batch is one statement, so the rejected row is detected by its absence
        // from what came back, not by a second round trip.
        await Assert.That(async () => await store.Store(client, expected, next, ct))
            .Throws<InvalidOperationException>();

        // …and the guard actually blocked the write rather than merely reporting it.
        var resumeFrom = await store.Load(client, Partitions(1), SubscriptionInitialPosition.Earliest, ct);

        await Assert.That(resumeFrom).IsDeepEquivalentTo(new[] { RecordPosition.From(Ahead, Partition.From(1)) });
    }

    #region ->> Arranging <<-

    static DuckDBProjectionCheckpointStore NewStore() =>
        new($"checkpoint-store-{TestId.New()}", TimeProvider.System);

    static Partition[] Partitions(params int[] values) =>
        values.Select(Partition.From).ToArray();

    /// <summary>
    /// Writes an initial position for each partition through the store's own <c>Store</c>, so the
    /// arrangement uses the same path production does rather than raw SQL that could drift from it.
    /// </summary>
    static ValueTask Seed(
        DuckDBProjectionCheckpointStore store,
        DuckDBAdvancedConnection client,
        Partition[] partitions,
        LogPosition position,
        CancellationToken cancellationToken
    ) =>
        store.Store(
            client, [],
            partitions.Select(partition => RecordPosition.From(position, partition)).ToArray(),
            cancellationToken);

    #endregion
}
