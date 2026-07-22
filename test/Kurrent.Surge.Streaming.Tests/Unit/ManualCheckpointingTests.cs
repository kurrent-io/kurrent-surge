using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Checkpointing;
using Kurrent.Surge.Streaming.Tests.Fakes;
using Kurrent.Testkit.TUnit.Assertions;

namespace Kurrent.Surge.Streaming.Tests;

[Category("Processors")]
public class ManualCheckpointingTests {
	static readonly RecordFaker Records = new();

	#region ->> ProcessRecordContext API <<-

	[Test]
	public async ValueTask checkpoint_throws_when_called_twice_in_same_process_record() {
		// Arrange
		var ctx = new ProcessRecordContext(_ => { }) {
			Record   = Records.Generate()
		};

		// Act
		ctx.Checkpoint("first");

		// Assert
        await Assert
            .That(() => ctx.Checkpoint("second"))
            .Throws<InvalidOperationException>();
    }

	[Test]
	public async ValueTask checkpoint_throws_operation_canceled_when_token_is_cancelled() {
		// Arrange
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		var ctx = new ProcessRecordContext(_ => { }) {
			Record            = Records.Generate(),
			CancellationToken = cts.Token
		};

		// Act & Assert
		await Assert.That(() => ctx.Checkpoint("x"))
			.Throws<OperationCanceledException>();
	}

	[Test]
	public async ValueTask checkpoint_stashes_request_for_drone_to_pick_up() {
		// Arrange
		var ctx = new ProcessRecordContext(_ => { }) {
			Record   = Records.Generate()
		};

		// Act
		ctx.Checkpoint("flushed-batch");

		// Assert
		await Assert.That(ctx.CheckpointRequested).IsTrue();
		await Assert.That(ctx.CheckpointReason).IsEqualTo("flushed-batch");
	}

	[Test]
	public async ValueTask track_record_forwards_record_position_to_request_track_delegate() {
		// Arrange
		var capturedPositions = new List<RecordPosition>();
		var record            = Records.Generate();

		var expectedPositions = new List<RecordPosition> { record.Position };

		var ctx = new ProcessRecordContext(capturedPositions.Add) {
			Record   = record
		};

		// Act
		ctx.Track(record);

		// Assert
		await Assert.That(capturedPositions).IsDeepEquivalentTo(expectedPositions);
	}

	[Test]
	public async ValueTask track_positions_forwards_each_position_to_request_track_delegate() {
		// Arrange
		var capturedPositions = new List<RecordPosition>();
		var positions         = Records.Generate(3).Select(r => r.Position).ToArray();

		var expectedPositions = positions.ToList();

		var ctx = new ProcessRecordContext(capturedPositions.Add) {
			Record   = Records.Generate()
		};

		// Act
		ctx.Track(positions);

		// Assert
		await Assert.That(capturedPositions).IsDeepEquivalentTo(expectedPositions);
	}

	#endregion // ProcessRecordContext API

	#region ->> PartitionedPositionTracker validation <<-

	[Test]
	public async ValueTask tracker_rejects_position_that_is_not_ahead_of_last_flushed() {
		// Arrange
		var partition = Partition.From(0);
		var first     = Records.Generate().Position with { Partition = partition };
		var second    = Records.Generate().Position with { Partition = partition };

		using var tracker = new PartitionedPositionTracker([second]);

		// Act — try to track a position equal to or below the initial flushed position.
		await Assert.That(() => tracker.Track(first)).Throws<InvalidOperationException>();
	}

	#endregion // PartitionedPositionTracker validation

}
