using Kurrent.Client.Streams;

namespace Kurrent.Streaming.Gateway;

public interface ISubscription : IAsyncDisposable {
	Task Subscribe(
		RecordPosition    startPosition,
		RecordFilter     filter,
		bool              skipDecoding,
		CancellationToken ct);
}
