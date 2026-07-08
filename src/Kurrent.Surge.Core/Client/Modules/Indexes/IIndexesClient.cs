namespace Kurrent.Client.Indexes;

public interface IIndexesClient {
    ValueTask Create(CreateIndexOptions options, CancellationToken cancellationToken = default);

    ValueTask Create(IndexName name, Action<CreateIndexOptions>? configure = null, CancellationToken cancellationToken = default);

    ValueTask Start(IndexName name, CancellationToken cancellationToken = default);

    ValueTask Stop(IndexName name, CancellationToken cancellationToken = default);

    ValueTask Delete(IndexName name, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<Index>> List(CancellationToken cancellationToken = default);

    ValueTask<Index> Get(IndexName name, CancellationToken cancellationToken = default);
}
