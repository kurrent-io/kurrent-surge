using Kurrent.Processors.Options;

namespace Kurrent.Processors.Tests.Unit.Settings;

[Category("Processors")]
public class CheckpointOptionsBuilderTests {
    #region Default Values

    [Test]
    public async ValueTask defaults_interval_is_30000() {
        // Arrange
        // Act
        var settings = new CheckpointOptions.Builder().Build();

        // Assert
        await Assert.That(settings.Interval).IsEqualTo(30000);
    }

    [Test]
    public async ValueTask defaults_timeout_is_30000() {
        // Arrange
        // Act
        var settings = new CheckpointOptions.Builder().Build();

        // Assert
        await Assert.That(settings.Timeout).IsEqualTo(30000);
    }

    [Test]
    public async ValueTask defaults_retention_count_is_3() {
        // Arrange
        // Act
        var settings = new CheckpointOptions.Builder().Build();

        // Assert
        await Assert.That(settings.RetentionCount).IsEqualTo(3);
    }

    [Test]
    public async ValueTask defaults_failure_policy_is_continue() {
        // Arrange
        // Act
        var settings = new CheckpointOptions.Builder().Build();

        // Assert
        await Assert.That(settings.FailurePolicy).IsEqualTo(CheckpointFailurePolicy.Continue);
    }

    #endregion

    #region Interval (long)

    [Test]
    public async ValueTask interval_long_sets_value() {
        // Arrange
        // Act
        var settings = new CheckpointOptions.Builder()
            .Interval(60000L)
            .Build();

        // Assert
        await Assert.That(settings.Interval).IsEqualTo(60000);
    }

    #endregion

    #region Interval (TimeSpan)

    [Test]
    public async ValueTask interval_timespan_sets_value_as_milliseconds() {
        // Arrange
        var interval = TimeSpan.FromMinutes(1);

        // Act
        var settings = new CheckpointOptions.Builder()
            .Interval(interval)
            .Build();

        // Assert
        await Assert.That(settings.Interval).IsEqualTo(60000);
    }

    #endregion

    #region Timeout (long)

    [Test]
    public async ValueTask timeout_long_sets_value() {
        // Arrange
        // Act
        var settings = new CheckpointOptions.Builder()
            .Timeout(10000L)
            .Build();

        // Assert
        await Assert.That(settings.Timeout).IsEqualTo(10000);
    }

    #endregion

    #region Timeout (TimeSpan)

    [Test]
    public async ValueTask timeout_timespan_sets_value_as_milliseconds() {
        // Arrange
        var timeout = TimeSpan.FromSeconds(15);

        // Act
        var settings = new CheckpointOptions.Builder()
            .Timeout(timeout)
            .Build();

        // Assert
        await Assert.That(settings.Timeout).IsEqualTo(15000);
    }

    #endregion

    #region RetentionCount

    [Test]
    public async ValueTask retention_count_sets_value() {
        // Arrange
        // Act
        var settings = new CheckpointOptions.Builder()
            .RetentionCount(10)
            .Build();

        // Assert
        await Assert.That(settings.RetentionCount).IsEqualTo(10);
    }

    #endregion

    #region FailurePolicy

    [Test]
    public async ValueTask failure_policy_sets_value() {
        // Arrange
        // Act
        var settings = new CheckpointOptions.Builder()
            .FailurePolicy(CheckpointFailurePolicy.FailProcessor)
            .Build();

        // Assert
        await Assert.That(settings.FailurePolicy).IsEqualTo(CheckpointFailurePolicy.FailProcessor);
    }

    #endregion

    #region Method Chaining

    [Test]
    public async ValueTask chained_calls_apply_all_values() {
        // Arrange
        // Act
        var settings = new CheckpointOptions.Builder()
            .Interval(5000L)
            .Timeout(2000L)
            .RetentionCount(5)
            .FailurePolicy(CheckpointFailurePolicy.FailProcessor)
            .Build();

        // Assert
        await Assert.That(settings.Interval).IsEqualTo(5000);
        await Assert.That(settings.Timeout).IsEqualTo(2000);
        await Assert.That(settings.RetentionCount).IsEqualTo(5);
        await Assert.That(settings.FailurePolicy).IsEqualTo(CheckpointFailurePolicy.FailProcessor);
    }

    #endregion
}
