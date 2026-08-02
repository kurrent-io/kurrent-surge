using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors.Transactional;

/// <summary>
/// Shared plumbing for every projection shape: schema bootstrap, resume, the per-batch handler, and the
/// checkpoint commit — all against the projection's own storage.
/// </summary>
/// <remarks>
/// <para>
/// A projection is an ordinary <see cref="IRecordBatchProcessor"/>. It differs from one only in claiming
/// its checkpoints during <c>OnInitialize</c>, which stops the drone writing its own checkpoint store and
/// makes this class the sole authority on where the subscription resumes.
/// </para>
/// <para>
/// The transaction opens on the first batch after a checkpoint and stays open across every batch until
/// the next checkpoint commits it. Batches write and track and never commit; the checkpoint writes the
/// tracked positions into that same transaction and commits, so projected rows and the positions
/// describing them go durable together.
/// </para>
/// <para>
/// There is no failure handling here, on purpose. Shapes track each record after applying it, so at any
/// instant the tracked frontier describes exactly the work sitting in the open transaction. When a batch
/// throws, the exception unwinds untouched and the drone's final checkpoint commits that frontier — the
/// applied prefix, rows and positions consistent. The record that failed is the first one replayed after
/// restart. Only the death of the connection itself discards work, and the backend's disposal rolls back
/// whatever was never committed.
/// </para>
/// </remarks>
[PublicAPI]
public abstract class ProjectionProcessor<TClient> : IRecordBatchProcessor where TClient : IAsyncDisposable {
    readonly List<ProjectionSetup<TClient>> _setupActions = [];

    IProjectionCheckpointStore<TClient>? _checkpoints;

    /// <summary>The provider a shape resolves its clients through.</summary>
    protected IProjectionClientProvider<TClient> Clients { get; private set; } = null!;

    /// <summary>Identifies this projection's rows in the checkpoint storage.</summary>
    protected string ProjectionId { get; private set; } = "";

    /// <summary>
    /// Supplies the transaction and checkpoint storage this projection commits through. Called once by a
    /// backend's projector, before the projection is handed to a drone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Public because a backend lives in its own assembly, not because it is a user-facing call. A
    /// projection that reaches a drone without this throws from <c>OnInitialize</c> rather than running
    /// with the framework's own checkpoint store — which would put its positions in one database and its
    /// projected state in another.
    /// </para>
    /// <para>
    /// Virtual so a shape that owns collaborators of its own — a document store, which has to write on the
    /// open transaction's client — can hand them the same transaction rather than resolving a second one.
    /// </para>
    /// </remarks>
    public virtual void Wire(
        string projectionId,
        IProjectionClientProvider<TClient> clients,
        IProjectionCheckpointStore<TClient> checkpoints
    ) {
        ProjectionId = projectionId;

        Clients      = clients;
        _checkpoints = checkpoints;
    }

    #region ->> Lifecycle <<-

    async ValueTask IProcessor.OnInitialize(InitializeContext context) {
        if (Clients is null || _checkpoints is null)
            throw new InvalidOperationException(
                $"{GetType().Name} was not wired. Create it through its backend projector rather than "
              + "registering it directly, or its checkpoints go to the wrong store.");

        await using var client = await Clients
            .GetClient(context.CancellationToken)
            .ConfigureAwait(false);

        await _checkpoints
            .EnsureStoreExists(client, context.CancellationToken)
            .ConfigureAwait(false);

        // The projection's own schema, and whatever it wants to assert about its handlers. This is the
        // only hook that runs before a single record is read, so it is where a projection that needs a
        // table, an index, or at least one registration says so. Registration order: a shape's own setup
        // was registered by its constructor, so it runs before anything the projection added.
        foreach (var setup in _setupActions)
            await setup(client, context.CancellationToken).ConfigureAwait(false);

        // An explicit start supersedes everything stored. Rewind first, then read back, so the resume
        // positions come from one place — reading the override directly would leave the stored rows
        // ahead of the subscription until the first checkpoint overwrote them.
        if (context.StartPosition != LogPosition.Unset) {
            // Only positions rewind. Projected rows stay where they are, so replaying over them is
            // safe only when every write is idempotent under replay.
            await _checkpoints
                .Reset(client, context.StartPosition, context.Partitions, context.CancellationToken)
                .ConfigureAwait(false);
        }

        var resumeFrom = await _checkpoints
            .Load(client, context.Partitions, context.InitialPosition, context.CancellationToken)
            .ConfigureAwait(false);

        context.OwnCheckpoints(resumeFrom);
    }

    ValueTask IRecordBatchProcessor.ProcessBatch(BatchContext context) =>
        OnBatch(context);

    ValueTask IProcessor.OnCheckpoint(CheckpointContext context) =>
        OnCheckpoint(context);

    #endregion

    #region ->> Setup <<-

    /// <summary>
    /// Registers work that runs once at startup — before the resume position is read and before the
    /// subscription opens. Create the projection's schema here, and assert whatever it needs to be
    /// usable: a table name, at least one registered handler, whatever "configured" means for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Additive, and deliberately so: a shape registers what it needs — a document projection's own table —
    /// and the projection deriving from it registers its own on top. Both run, in registration order. A
    /// single slot would let the second registration silently unhook the first, and the symptom would be a
    /// missing table on the next run rather than an error here.
    /// </para>
    /// <para>
    /// The framework deliberately does not check that a projection registered any handlers. What counts
    /// as enough differs per shape, and only the projection knows. A projection that asserts nothing and
    /// matches nothing will consume its subscription and project silently.
    /// </para>
    /// </remarks>
    protected void Setup(ProjectionSetup<TClient> handler) => _setupActions.Add(handler);

    /// <inheritdoc cref="Setup(ProjectionSetup{TClient})"/>
    protected void Setup(Action<TClient> handler) =>
        _setupActions.Add((client, _) => {
            handler(client);
            return ValueTask.CompletedTask;
        });

    #endregion

    /// <summary>Applies one batch through the client of the currently open transaction.</summary>
    /// <remarks>
    /// Track each record after applying it, so the frontier always equals the applied work — that
    /// equality is what makes the final checkpoint after a mid-batch failure commit a consistent prefix.
    /// The built-in shapes already do this.
    /// </remarks>
    public abstract ValueTask OnBatch(BatchContext context);

    /// <summary>
    /// Runs on every checkpoint, inside the open transaction and before the positions are stored. Write
    /// anything the shape is holding here: it goes durable with those positions or not at all.
    /// </summary>
    /// <remarks>
    /// This does not commit. The checkpoint commits immediately after, which is what makes the rows and
    /// the positions one durable fact.
    /// </remarks>
    protected virtual ValueTask OnCheckpoint(CheckpointContext context) => default;
}

/// <summary>
/// Work a projection runs once at startup, before the resume position is read and before the subscription
/// opens — creating its schema, asserting whatever it needs to be usable.
/// </summary>
/// <remarks>
/// Registered through <see cref="ProjectionProcessor{TClient}.Setup(ProjectionSetup{TClient})"/>, which is
/// additive: a shape and the projection deriving from it both register, and both run in registration order.
/// </remarks>
public delegate ValueTask ProjectionSetup<in TClient>(TClient client, CancellationToken cancellationToken);
