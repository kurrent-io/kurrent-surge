using Kurrent.Client.Exceptions;
using static KurrentDB.Protocol.V2.Streams.StreamsService;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    public StreamsAppender NewAppender(CancellationToken ct = default) => new(Mapper, ServiceClient, ct);
}

[PublicAPI]
public class StreamsAppender : IAsyncDisposable {
    readonly StreamsProtocolMapper _mapper;
    readonly StreamsServiceClient  _serviceClient;
    readonly CancellationToken     _ct;

    internal StreamsAppender(StreamsProtocolMapper mapper, StreamsServiceClient serviceClient, CancellationToken ct) {
        _mapper        = mapper;
        _serviceClient = serviceClient;
        _ct            = ct;
    }

    public AppendRecordsRequestBuilder NewRequestBuilder() => new();

    public async ValueTask<AppendRecordsResponse> AppendAsync(AppendRecordsRequest request, CancellationToken ct) {
        if (request.Records.Length == 0)
            throw new InvalidOperationException("No records to append. Add at least one record before calling AppendAsync.");

        var missingStream = request.Records.FirstOrDefault(r => r.Stream == StreamName.Unset);
        if (missingStream is not null)
            throw new InvalidOperationException($"Record '{missingStream.RecordId}' has no stream assigned. Set the stream on each record or use ForStream on the builder.");

        try {
            var contract = await _mapper
                .ToContract(request, ct)
                .ConfigureAwait(false);

            var response = await _serviceClient
                .AppendRecordsAsync(contract, cancellationToken: ct)
                .ConfigureAwait(false);

            return _mapper.ToAppendRecordsResponse(response);
        }
        catch (Exception ex) {
            throw ex.ToException(AppendConsistencyViolationException.From, MaxAppendRecordSizeExceededException.From, MaxAppendTransactionSizeExceededException.From);
        }
    }

    public async ValueTask<AppendRecordsResponse> AppendAsync(Action<AppendRecordsRequestBuilder> configure, CancellationToken ct) {
        var builder = NewRequestBuilder();
        configure(builder);
        return await AppendAsync(builder.Build(), ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
