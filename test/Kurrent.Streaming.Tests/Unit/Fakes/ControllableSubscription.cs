using System.Threading.Channels;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Gateway;
using Kurrent.Streaming.Models;

namespace Kurrent.Streaming.Tests.Fakes;

/// <summary>
/// In-memory subscription that lets tests inject specific messages into the drone's
/// channel. Implements <see cref="ISubscription"/> by pumping from an internal inbox
/// to the drone's channel writer.
/// </summary>
sealed class ControllableSubscription : ISubscription {
	readonly Channel<SubscribeMessage> _inbox = Channel.CreateUnbounded<SubscribeMessage>();
	ChannelWriter<SubscribeMessage>? _droneWriter;

	/// <summary>
	/// Sets the drone's channel writer. Must be called before Subscribe.
	/// </summary>
	public void SetWriter(ChannelWriter<SubscribeMessage> writer) => _droneWriter = writer;

	public async Task Subscribe(
		RecordPosition startPosition, RecordFilter filter,
		bool skipDecoding, CancellationToken ct
	) {
		var writer = _droneWriter ?? throw new InvalidOperationException("Writer not set");

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

	public ValueTask DisposeAsync() {
		_inbox.Writer.TryComplete();
		return ValueTask.CompletedTask;
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
