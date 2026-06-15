using Kurrent.Processors.Configuration;
using Kurrent.Streaming.Options;

namespace Kurrent.Streaming.Tests.Options;

[Category("Processors")]
public class CheckpointOptionsTests {
    #region ->> Default Values <<-

    [Test]
    public async ValueTask defaults_auto_track_is_true() {
        // Arrange & Act
        var options = new CheckpointOptions();

        // Assert
        await Assert.That(options.AutoTrack).IsTrue();
    }

    [Test]
    public async ValueTask defaults_auto_checkpoint_is_true() {
        // Arrange & Act
        var options = new CheckpointOptions();

        // Assert
        await Assert.That(options.AutoCheckpoint).IsTrue();
    }

    [Test]
    public async ValueTask defaults_interval_is_30_seconds() {
        // Arrange & Act
        var options = new CheckpointOptions();

        // Assert
        await Assert.That(options.Interval).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async ValueTask defaults_retention_count_is_3() {
        // Arrange & Act
        var options = new CheckpointOptions();

        // Assert
        await Assert.That(options.RetentionCount).IsEqualTo(3);
    }

    [Test]
    public async ValueTask defaults_failure_policy_is_continue() {
        // Arrange & Act
        var options = new CheckpointOptions();

        // Assert
        await Assert.That(options.FailurePolicy).IsEqualTo(CheckpointFailurePolicy.Continue);
    }

    #endregion // Default Values

    #region ->> AutoTrack <<-

    [Test]
    public async ValueTask auto_track_sets_value() {
        // Arrange & Act
        var options = new CheckpointOptions { AutoTrack = false };

        // Assert
        await Assert.That(options.AutoTrack).IsFalse();
    }

    #endregion // AutoTrack

    #region ->> AutoCheckpoint <<-

    [Test]
    public async ValueTask auto_checkpoint_sets_value() {
        // Arrange & Act
        var options = new CheckpointOptions { AutoCheckpoint = false };

        // Assert
        await Assert.That(options.AutoCheckpoint).IsFalse();
    }

    #endregion // AutoCheckpoint

    #region ->> IntervalFrom <<-

    [Test]
    public async ValueTask interval_from_timespan_sets_value() {
        // Arrange
        var options  = new CheckpointOptions();
        var expected = TimeSpan.FromMinutes(1);

        // Act
        options.IntervalFrom(expected);

        // Assert
        await Assert.That(options.Interval).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask interval_from_milliseconds_sets_value() {
        // Arrange
        var options  = new CheckpointOptions();
        var expected = TimeSpan.FromMilliseconds(60000);

        // Act
        options.IntervalFrom(60000L);

        // Assert
        await Assert.That(options.Interval).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask interval_from_returns_same_instance_for_chaining() {
        // Arrange
        var options = new CheckpointOptions();

        // Act
        var returned = options.IntervalFrom(TimeSpan.FromSeconds(5));

        // Assert
        await Assert.That(returned).IsSameReferenceAs(options);
    }

    #endregion // IntervalFrom
}
