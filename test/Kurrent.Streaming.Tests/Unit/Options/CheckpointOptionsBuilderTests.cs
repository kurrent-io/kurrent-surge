using Kurrent.Streaming.Options;

namespace Kurrent.Streaming.Tests.Options;

[Category("Processors")]
public class CheckpointOptionsBuilderTests {
    #region ->> Default Values <<-

    [Test]
    public async ValueTask defaults_interval_is_30000() {
        // Arrange & Act
        var settings = new CheckpointOptions();

        // Assert
        await Assert.That(settings.Interval).IsEqualTo(TimeSpan.FromMilliseconds(30000));
    }

    [Test]
    public async ValueTask defaults_failure_policy_is_continue() {
        // Arrange &Act
        var settings = new CheckpointOptions();

        // Assert
        await Assert.That(settings.FailurePolicy).IsEqualTo(CheckpointFailurePolicy.Continue);
    }

    #endregion // Default Values

    #region ->> Interval (long) <<-

    [Test]
    public async ValueTask interval_long_sets_value() {
        // Arrange
        var settings = new CheckpointOptions();

        // Act
        new CheckpointOptions.Builder(settings)
            .Interval(60000L);

        // Assert
        await Assert.That(settings.Interval).IsEqualTo(TimeSpan.FromMilliseconds(60000));
    }

    #endregion // Interval (long)

    #region ->> Interval (TimeSpan) <<-

    [Test]
    public async ValueTask interval_timespan_sets_value_as_milliseconds() {
        // Arrange
        var interval = TimeSpan.FromMinutes(1);
        var settings = new CheckpointOptions();

        // Act
        new CheckpointOptions.Builder(settings)
            .Interval(interval);

        // Assert
        await Assert.That(settings.Interval).IsEqualTo(TimeSpan.FromMilliseconds(60000));
    }

    #endregion // Interval (TimeSpan)

    #region ->> FailurePolicy <<-

    [Test]
    public async ValueTask failure_policy_sets_value() {
        // Arrange
        var settings = new CheckpointOptions();

        // Act
        new CheckpointOptions.Builder(settings)
            .FailurePolicy(CheckpointFailurePolicy.Fail);

        // Assert
        await Assert.That(settings.FailurePolicy).IsEqualTo(CheckpointFailurePolicy.Fail);
    }

    #endregion // FailurePolicy

    #region ->> Method Chaining <<-

    [Test]
    public async ValueTask chained_calls_apply_all_values() {
        // Arrange
        var settings = new CheckpointOptions();

        // Act
        new CheckpointOptions.Builder(settings)
            .Interval(5000L)
            .RetentionCount(5)
            .FailurePolicy(CheckpointFailurePolicy.Fail);

        // Assert
        await Assert.That(settings.Interval).IsEqualTo(TimeSpan.FromMilliseconds(5000));
        await Assert.That(settings.RetentionCount).IsEqualTo(5);
        await Assert.That(settings.FailurePolicy).IsEqualTo(CheckpointFailurePolicy.Fail);
    }

    #endregion // Method Chaining
}
