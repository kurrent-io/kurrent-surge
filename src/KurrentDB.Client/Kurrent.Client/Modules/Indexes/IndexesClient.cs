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
    /// <param name="name">The unique name of the index to create.</param>
    /// <param name="filter">The filter expression that determines which events are included in the index.</param>
    /// <param name="fields">The fields to index, each with a name, selector, and type.</param>
    /// <param name="start">Whether to start the index immediately after creation. Defaults to true if not specified.</param>
    /// <param name="cancellationToken">
    /// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    /// </param>
    /// <returns>
    /// A <see cref="Result{Success, CreateIndexError}"/> indicating the outcome of the index creation.
    /// </returns>
    public async ValueTask<Result<Success, CreateIndexError>> Create(
        string name,
        string filter,
        IEnumerable<IndexField> fields,
        bool? start = null,
        CancellationToken cancellationToken = default
    ) {
        var request = new Contracts.CreateIndexRequest {
            Name   = name,
            Filter = filter,
            Fields = { fields.Select(f => f.ToProto()) }
        };

        if (start.HasValue)
            request.Start = start.Value;

        try {
            _ = await ServiceClient
                .CreateAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            return ex.ToExceptionalErrorResult<CreateIndexError>();
        }
    }

    /// <summary>
    /// Starts an existing secondary index.
    /// </summary>
    /// <param name="name">The name of the index to start.</param>
    /// <param name="cancellationToken">
    /// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    /// </param>
    /// <returns>
    /// A <see cref="Result{Success, StartIndexError}"/> indicating the outcome of the operation.
    /// </returns>
    public async ValueTask<Result<Success, StartIndexError>> Start(
        string name,
        CancellationToken cancellationToken = default
    ) {
        var request = new Contracts.StartIndexRequest { Name = name };

        try {
            _ = await ServiceClient
                .StartAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            return ex.ToExceptionalErrorResult<StartIndexError>();
        }
    }

    /// <summary>
    /// Stops an existing secondary index.
    /// </summary>
    /// <param name="name">The name of the index to stop.</param>
    /// <param name="cancellationToken">
    /// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    /// </param>
    /// <returns>
    /// A <see cref="Result{Success, StopIndexError}"/> indicating the outcome of the operation.
    /// </returns>
    public async ValueTask<Result<Success, StopIndexError>> Stop(
        string name,
        CancellationToken cancellationToken = default
    ) {
        var request = new Contracts.StopIndexRequest { Name = name };

        try {
            _ = await ServiceClient
                .StopAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            return ex.ToExceptionalErrorResult<StopIndexError>();
        }
    }

    /// <summary>
    /// Deletes an existing secondary index.
    /// </summary>
    /// <param name="name">The name of the index to delete.</param>
    /// <param name="cancellationToken">
    /// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    /// </param>
    /// <returns>
    /// A <see cref="Result{Success, DeleteIndexError}"/> indicating the outcome of the operation.
    /// </returns>
    public async ValueTask<Result<Success, DeleteIndexError>> Delete(
        string name,
        CancellationToken cancellationToken = default
    ) {
        var request = new Contracts.DeleteIndexRequest { Name = name };

        try {
            _ = await ServiceClient
                .DeleteAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            return ex.ToExceptionalErrorResult<DeleteIndexError>();
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
    public async ValueTask<Result<IReadOnlyList<Index>, ListIndexesError>> List(
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
            return ex.ToExceptionalErrorResult<ListIndexesError>();
        }
    }

    /// <summary>
    /// Gets the details of a specific secondary index.
    /// </summary>
    /// <param name="name">The name of the index to retrieve.</param>
    /// <param name="cancellationToken">
    /// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    /// </param>
    /// <returns>
    /// A <see cref="Result{Index, GetIndexError}"/> containing the index details or an error.
    /// </returns>
    public async ValueTask<Result<Index, GetIndexError>> Get(
        string name,
        CancellationToken cancellationToken = default
    ) {
        var request = new Contracts.GetIndexRequest { Name = name };

        try {
            var response = await ServiceClient
                .GetAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Index.FromProto(response.Index);
        }
        catch (Exception ex) {
            return ex.ToExceptionalErrorResult<GetIndexError>();
        }
    }
}
