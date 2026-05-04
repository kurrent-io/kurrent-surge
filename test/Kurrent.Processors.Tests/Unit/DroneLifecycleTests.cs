using System.Threading.Channels;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Options;
using Kurrent.Processors.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Processors.Tests;

[Category("Processors")]
public class DroneLifecycleTests {
	static (Drone drone, ControllableSubscription subscription) CreateDrone(
		IRecordProcessor? processor = null,
		bool              debug     = false
	) {
		processor ??= new TestableRecordProcessor();

		var settings = new ProcessorOptions {
			ProcessorId = "test-processor",
			DroneId     = "test-drone",
			Debug       = debug
		};

		var channel = Channel.CreateUnbounded<SubscribeMessage>();

		var store = new InMemoryCheckpointStore();

		var checkpointer = new Checkpointer(
			settings.DroneId, settings.Checkpoint,
			store, channel.Writer, TimeProvider.System);

		var subscription = new ControllableSubscription();
		subscription.SetWriter(channel.Writer);

		var drone = new Drone(
			settings, subscription, checkpointer, channel,
			null!, // StreamsClient not needed for lifecycle tests
			NullLoggerFactory.Instance, TimeProvider.System,
			processor);

		return (drone, subscription);
	}

	#region ->> cleanup <<-

	[Test]
	public async ValueTask cleanup_runs_on_normal_completion() {
		// Arrange
		var (drone, subscription) = CreateDrone();

		// Act — empty subscription completes immediately
		subscription.Complete();
		await drone.RunAsync(CancellationToken.None);

		// Assert
		await Assert.That(drone.Status).IsEqualTo(DroneStatus.Stopped);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask cleanup_runs_on_cancellation(CancellationToken ct) {
		// Arrange
		var cts = new CancellationTokenSource();
		var (drone, subscription) = CreateDrone();

		// Act — pre-cancelled token; subscription reads the token and completes
		await cts.CancelAsync();
		subscription.Complete();
		await drone.RunAsync(cts.Token);

		// Assert
		await Assert.That(drone.Status).IsEqualTo(DroneStatus.Stopped);
	}

	[Test]
	public async ValueTask cleanup_is_idempotent() {
		// Arrange
		var (drone, subscription) = CreateDrone();
		subscription.Complete();

		// Act — first call triggers cleanup via RunAsync
		await drone.RunAsync(CancellationToken.None);

		// Assert — second call throws started guard, not double-dispose
		await Assert.That(async () => await drone.RunAsync(CancellationToken.None))
			.Throws<InvalidOperationException>();
	}

	#endregion

	#region ->> run_async guards <<-

	[Test]
	public async ValueTask run_async_throws_if_called_twice() {
		// Arrange
		var (drone, subscription) = CreateDrone();
		subscription.Complete();
		await drone.RunAsync(CancellationToken.None);

		// Act & Assert
		await Assert.That(async () => await drone.RunAsync(CancellationToken.None))
			.Throws<InvalidOperationException>();
	}

	#endregion

	#region ->> status transitions <<-

	[Test]
	public async ValueTask status_is_stopped_on_clean_shutdown() {
		// Arrange
		var (drone, subscription) = CreateDrone();
		subscription.Complete();

		// Act
		await drone.RunAsync(CancellationToken.None);

		// Assert
		await Assert.That(drone.Status).IsEqualTo(DroneStatus.Stopped);
	}

	#endregion

}
