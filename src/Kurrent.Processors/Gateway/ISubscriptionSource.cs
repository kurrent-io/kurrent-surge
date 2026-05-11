using Kurrent.Client.Streams;

namespace Kurrent.Processors.Gateway;

public interface ISubscription : IAsyncDisposable {
	Task Subscribe(
		RecordPosition    startPosition,
		IRecordFilter     filter,
		bool              skipDecoding,
		CancellationToken ct);
}
