using Kurrent.Client.Exceptions;

using static KurrentDB.Protocol.V2.Indexes.IndexesService;
using Contracts = KurrentDB.Protocol.V2.Indexes;

namespace Kurrent.Client.Indexes;

[PublicAPI]
public class IndexesClient : ClientModuleBase {
    internal IndexesClient(KurrentClient client) : base(client) =>
        ServiceClient = new(client.LegacyCallInvoker);

    IndexesServiceClient ServiceClient { get; }

    /// <summary>
    /// Creates a new secondary index with the specified configuration.
    /// </summary>
    /// <param name="request">The index details to create. Use <see cref="CreateIndexRequest.For"/> to build one fluently.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async ValueTask Create(CreateIndexRequest request, CancellationToken cancellationToken = default) {
        request.EnsureValid();

        var rpc = new Contracts.CreateIndexRequest {
            Name   = request.Name,
            Filter = request.Filter,
            Fields = { request.Fields.Select(f => f.ToProto()) }
        };

        if (request.Start.HasValue)
            rpc.Start = request.Start.Value;

        try {
            _ = await ServiceClient
                .CreateAsync(rpc, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }
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
            throw ex.AsWellKnownException();
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
            throw ex.AsWellKnownException();
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
            throw ex.AsWellKnownException();
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

            return response.Indexes.Select(Index.FromProto).ToList();
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
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
            throw ex.AsWellKnownException();
        }
    }
}
