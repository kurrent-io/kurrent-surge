using System.Threading.Channels;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Gateway;

namespace Kurrent.Streaming.Tests.Fakes;

/// <summary>
/// In-memory data source that lets tests inject specific messages into the drone's channel.
/// Implements <see cref="IDataSource"/> as a plain pump from an internal inbox to the drone's
/// channel writer. It does NOT prune — pruning is the real <c>KdbDataSource</c>'s job (covered by
/// its own tests) — so every emitted message reaches the drone verbatim.
/// </summary>
sealed class ControllableDataSource : IDataSource {
	readonly Channel<DataSourceMessage> _inbox = Channel.CreateUnbounded<DataSourceMessage>();

	// startPositions are ignored: a plain pump delivers exactly what tests emit. The drone no longer
	// prunes, and this fake stands in for a source that already did.
	public async Task Connect(ChannelWriter<DataSourceMessage> writer, RecordPosition[] startPositions, CancellationToken ct) {
		try {
			await foreach (var msg in _inbox.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
				await writer.WriteAsync(msg, CancellationToken.None).ConfigureAwait(false);

			writer.TryComplete();
		}
		catch (OperationCanceledException) {
			writer.TryComplete();
		}
		catch (Exception ex) {
			writer.TryComplete(ex);
		}
	}

	public void EmitConnected() =>
		_inbox.Writer.TryWrite(new Connected(SessionId.From(Guid.NewGuid())));

	public void EmitRecord(Record record) =>
		_inbox.Writer.TryWrite(record);

	public void EmitCheckpoint(Checkpoint ckpt) =>
		_inbox.Writer.TryWrite(ckpt);

	public void Complete() =>
		_inbox.Writer.TryComplete();
}
