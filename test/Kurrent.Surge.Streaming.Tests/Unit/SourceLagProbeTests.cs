using Kurrent.Surge.Processors;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Tests.Fakes;
using Kurrent.Testkit.TUnit.Assertions;
using Microsoft.Extensions.Time.Testing;

namespace Kurrent.Surge.Streaming.Tests;

[Category("Processors")]
public class SourceLagProbeTests {
	static readonly RecordFaker Records = new();

	[Test]
	[Timeout(15_000)]
	public async ValueTask source_lag_computes_from_probed_head() {
		// Arrange — a fake clock drives the probe; a stubbed head read returns a head 800 bytes and
		// 5 seconds ahead of the committed / last-processed position.
		var time      = new FakeTimeProvider();
		var headTime  = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var head      = Records.Generate() with {
			Position  = RecordPosition.From(LogPosition.From(1000), Partition.From(0)),
			Timestamp = headTime
		};
		var committed = RecordPosition.From(LogPosition.From(200), Partition.From(0));
		var progress  = DroneProgress.Empty with {
			LastProcessedAt          = headTime.AddSeconds(-5),
			LowestCommittedPosition  = committed,
			HighestCommittedPosition = committed
		};

		long?   lagBytes   = null;
		double? lagSeconds = null;

		await using var probe = new SourceLagProbe(
			readHead: _ => ValueTask.FromResult(head),
			snapshot: () => progress,
			onSample: (bytes, seconds) => { lagBytes = bytes; lagSeconds = seconds; },
			timeProvider: time);

		// Act — advance past the fixed 10s refresh so the probe samples once.
		probe.Start();
		time.Advance(TimeSpan.FromSeconds(10));

		await Assert.That(() => lagBytes.HasValue).EventuallyPasses(captured => captured.IsTrue());

		// Assert — byte gap is head − committed; second gap is head − last-processed event-time.
		await Assert.That(lagBytes!.Value).IsEqualTo(800L);
		await Assert.That(lagSeconds!.Value).IsEqualTo(5d).Within(0.001);
	}
}
