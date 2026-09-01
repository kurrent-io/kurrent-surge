using Kurrent.Surge.Client.Exceptions;

using static KurrentDB.Protocol.V2.Indexes.IndexesService;
using Contracts = KurrentDB.Protocol.V2.Indexes;

namespace Kurrent.Surge.Client.Indexes;

[PublicAPI]
public sealed class IndexesClient : ClientModuleBase, IIndexesClient {
    internal IndexesClient(KurrentClient client) : base(client) =>
        ServiceClient = new(client.CallInvoker);

    IndexesServiceClient ServiceClient { get; }

    static readonly OperationExceptionParser[] IndexErrors = [
        IndexNotFoundException.Match,
        IndexAlreadyExistsException.Match,
        IndexesNotReadyException.Match,
        SecondaryIndexingDisabledException.Match
    ];

    /// <summary>
    /// Creates a new secondary index with the specified configuration.
    /// </summary>
    /// <param name="options">The index details to create.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async ValueTask Create(CreateIndexOptions options, CancellationToken cancellationToken = default) {
        options.EnsureValid();

        var rpc = new Contracts.CreateIndexRequest {
            Name   = options.Name,
            Filter = options.Filter,
            Fields = { options.Fields.Select(f => f.ToProto()) }
        };

        if (options.Start.HasValue)
            rpc.Start = options.Start.Value;

        try {
            _ = await ServiceClient
                .CreateAsync(rpc, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException(IndexErrors);
        }
    }

    /// <summary>
    /// Creates a new secondary index for the given name, configured inline from a fresh
    /// <see cref="CreateIndexOptions"/>.
    /// </summary>
    /// <param name="name">The name of the index to create.</param>
    /// <param name="configure">Configures the options for the index.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public ValueTask Create(
        IndexName name,
        Action<CreateIndexOptions>? configure = null,
        CancellationToken cancellationToken = default
    ) {
        var options = new CreateIndexOptions { Name = name };
        configure?.Invoke(options);
        return Create(options, cancellationToken);
    }

    /// <summary>
    /// Starts an existing secondary index.
    /// </summary>
    /// <param name="name">The name of the index to start.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async ValueTask Start(IndexName name, CancellationToken cancellationToken = default) {
        if (name.IsUnset)
            throw new ArgumentException("Index name cannot be empty.", nameof(name));

        var request = new Contracts.StartIndexRequest { Name = name };

        try {
            _ = await ServiceClient
                .StartAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException(IndexErrors);
        }
    }

    /// <summary>
    /// Stops an existing secondary index.
    /// </summary>
    /// <param name="name">The name of the index to stop.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async ValueTask Stop(IndexName name, CancellationToken cancellationToken = default) {
        if (name.IsUnset)
            throw new ArgumentException("Index name cannot be empty.", nameof(name));

        var request = new Contracts.StopIndexRequest { Name = name };

        try {
            _ = await ServiceClient
                .StopAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException(IndexErrors);
        }
    }

    /// <summary>
    /// Deletes an existing secondary index.
    /// </summary>
    /// <param name="name">The name of the index to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async ValueTask Delete(IndexName name, CancellationToken cancellationToken = default) {
        if (name.IsUnset)
            throw new ArgumentException("Index name cannot be empty.", nameof(name));

        var request = new Contracts.DeleteIndexRequest { Name = name };

        try {
            _ = await ServiceClient
                .DeleteAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException(IndexErrors);
        }
    }

    /// <summary>
    /// Lists all secondary indexes.
    /// </summary>
    /// <param name="cancellationToken">
    /// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    /// </param>
    /// <returns>
    /// A <see cref="Result{IReadOnlyList, ListIndexesError}"/> containing the list of indexes or an error.
    /// </returns>
    public async ValueTask<IReadOnlyList<Index>> List(
        CancellationToken cancellationToken = default
    ) {
        var request = new Contracts.ListIndexesRequest();

        try {
            var response = await ServiceClient
                .ListAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Indexes.Select(i => Index.FromProto(i)).ToList();
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException(IndexErrors);
        }
    }

    /// <summary>
    /// Gets the details of a specific secondary index.
    /// </summary>
    /// <param name="name">The name of the index to retrieve.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The <see cref="Index"/> details.</returns>
    public async ValueTask<Index> Get(IndexName name, CancellationToken cancellationToken = default) {
        if (name.IsUnset)
            throw new ArgumentException("Index name cannot be empty.", nameof(name));

        var request = new Contracts.GetIndexRequest { Name = name };

        try {
            var response = await ServiceClient
                .GetAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Index.FromProto(response.Index);
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException(IndexErrors);
        }
    }
}
