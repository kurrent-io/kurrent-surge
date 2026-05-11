using System.Threading.Channels;
using Kurrent.Client;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Checkpointing;
using Kurrent.Streaming.Gateway;
using Kurrent.Streaming.Options;

namespace Kurrent.Streaming;

/// <summary>
/// Entry point for configuring and running a Kurrent processor. Use one of the
/// <c>Create</c> factories to produce a configured runner, then call
/// <see cref="RunAsync"/> or <see cref="RunWithGracefulShutdownAsync"/>.
/// </summary>
[PublicAPI]
public sealed class KurrentProcessor {
	readonly ProcessorOptions _options;
	readonly IProcessor       _processor;

	internal KurrentProcessor(ProcessorOptions options, IProcessor processor) {
		_options   = options;
		_processor = processor;
	}

	/// <summary>
	/// Creates a processor from a prebuilt <see cref="IRecordProcessor"/>.
	/// </summary>
	public static KurrentProcessor Create(IRecordProcessor processor, ProcessorOptions options) {
		ArgumentNullException.ThrowIfNull(processor);
		return new KurrentProcessor(options, processor);
	}

	public static KurrentProcessor Create(IRecordProcessor processor, Action<ProcessorOptions>? configure = null) {
		ArgumentNullException.ThrowIfNull(processor);

		var options = new ProcessorOptions();
		configure?.Invoke(options);
		return Create(processor, options);
	}

	/// <summary>
	/// Creates a processor from a prebuilt <see cref="IRecordBatchProcessor"/>.
	/// </summary>
	public static KurrentProcessor Create(IRecordBatchProcessor processor, ProcessorOptions options) {
		ArgumentNullException.ThrowIfNull(processor);
		return new KurrentProcessor(options, processor);
	}

	public static KurrentProcessor Create(IRecordBatchProcessor processor, Action<ProcessorOptions>? configure = null) {
		ArgumentNullException.ThrowIfNull(processor);

		var options = new ProcessorOptions();
		configure?.Invoke(options);
		return Create(processor, options);
	}

	/// <summary>
	/// Creates a processor using an inline <see cref="MessageProcessor"/> composed via
	/// the <paramref name="handlers"/> callback.
	/// </summary>
	public static KurrentProcessor Create(Action<MessageProcessor> handlers, Action<ProcessorOptions>? configure = null) {
		ArgumentNullException.ThrowIfNull(handlers);

		var processor = new MessageProcessor();
		handlers(processor);
		return Create(processor, configure);
	}

	/// <summary>
	/// Builds the processor drone for runtime access and control. Validates options and
	/// constructs the drone without starting it. Throws if
	/// <see cref="ProcessorOptions.ClientOptions"/> has not been set.
	/// </summary>
	internal IDrone Build() {
		if (string.IsNullOrWhiteSpace(_options.ProcessorId))
			_options.ProcessorId = _processor.GetType().Name.ToKebabCase();

		if (_options.ClientOptions is null)
			throw new InvalidOperationException(
				$"No connection configured for processor '{_options.ProcessorId}'. " +
				"Call opts.Connection(...) before building the processor.");

		_options.EnsureValid();

		var client = new KurrentClient(_options.ClientOptions);

		var channel = Channel.CreateBounded<SubscribeMessage>(
			new BoundedChannelOptions(_options.BufferCapacity) {
				FullMode     = BoundedChannelFullMode.Wait,
				SingleReader = true,
				SingleWriter = false
			});

		var checkpointStore = new KurrentDBCheckpointStore(
			_options.ProcessorId,
			_options.MaxParallelism,
			_options.Checkpoint.RetentionCount,
			client.Streams,
			client.Options.Schema.Mapper,
			TimeProvider.System,
			NullLogger<KurrentDBCheckpointStore>.Instance
		);

		var checkpointer = new Checkpointer(
			_options.DroneId,
			_options.Checkpoint,
			checkpointStore,
			channel.Writer,
			TimeProvider.System
		);

		var subscription = new SubscriptionSource(client, _options, channel.Writer);

		return new Drone(
			_options,
			subscription,
			checkpointer,
			channel,
			new StreamsClient(client),
			client.Options.LoggerFactory,
			TimeProvider.System,
			_processor
		);
	}

	/// <summary>
	/// Runs the processor until cancellation is requested.
	/// </summary>
	public async ValueTask RunAsync(CancellationToken stoppingToken = default) {
		var drone = Build();
		await drone.RunAsync(stoppingToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Runs the processor with automatic graceful shutdown on SIGINT/SIGTERM.
	/// </summary>
	public async ValueTask RunWithGracefulShutdownAsync(TimeSpan? shutdownTimeout = null) {
		var timeout          = shutdownTimeout ?? TimeSpan.FromSeconds(30);
		var cancellator      = new CancellationTokenSource();
		var shutdownComplete = new ManualResetEventSlim(false);

		Console.CancelKeyPress += OnCancelKeyPress;
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

		try {
			await RunAsync(cancellator.Token).ConfigureAwait(false);
		}
		finally {
			Console.CancelKeyPress -= OnCancelKeyPress;
			AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

			shutdownComplete.Set();
			cancellator.Dispose();
			shutdownComplete.Dispose();
		}

		return;

		void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) {
			e.Cancel = true;
			try { cancellator.Cancel(); } catch (ObjectDisposedException) { }
		}

		void OnProcessExit(object? sender, EventArgs e) {
			try { cancellator.Cancel(); } catch (ObjectDisposedException) { }
			try { shutdownComplete.Wait(timeout); } catch (ObjectDisposedException) { }
		}
	}
}
