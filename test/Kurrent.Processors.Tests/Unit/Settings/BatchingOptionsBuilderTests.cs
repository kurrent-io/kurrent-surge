using Kurrent.Processors.Options;

namespace Kurrent.Processors.Tests.Unit.Settings;

[Category("Processors")]
public class BatchingOptionsBuilderTests {
    #region Default Values

    [Test]
    public async ValueTask defaults_max_size_is_200() {
        // Arrange
        // Act
        var settings = new BatchingOptions.Builder().Build();

        // Assert
        await Assert.That(settings.MaxSize).IsEqualTo(200);
    }

    [Test]
    public async ValueTask defaults_timeout_is_250() {
        // Arrange
        // Act
        var settings = new BatchingOptions.Builder().Build();

        // Assert
        await Assert.That(settings.Timeout).IsEqualTo(250);
    }

    #endregion

    #region MaxSize

    [Test]
    public async ValueTask max_size_sets_value() {
        // Arrange
        // Act
        var settings = new BatchingOptions.Builder()
            .MaxSize(500)
            .Build();

        // Assert
        await Assert.That(settings.MaxSize).IsEqualTo(500);
    }

    #endregion

    #region Timeout (long)

    [Test]
    public async ValueTask timeout_long_sets_value() {
        // Arrange
        // Act
        var settings = new BatchingOptions.Builder()
            .Timeout(1000L)
            .Build();

        // Assert
        await Assert.That(settings.Timeout).IsEqualTo(1000);
    }

    #endregion

    #region Timeout (TimeSpan)

    [Test]
    public async ValueTask timeout_timespan_sets_value_as_milliseconds() {
        // Arrange
        var timeout = TimeSpan.FromSeconds(2);

        // Act
        var settings = new BatchingOptions.Builder()
            .Timeout(timeout)
            .Build();

        // Assert
        await Assert.That(settings.Timeout).IsEqualTo(2000);
    }

    #endregion

    #region Method Chaining

    [Test]
    public async ValueTask chained_calls_apply_all_values() {
        // Arrange
        // Act
        var settings = new BatchingOptions.Builder()
            .MaxSize(100)
            .Timeout(500L)
            .Build();

        // Assert
        await Assert.That(settings.MaxSize).IsEqualTo(100);
        await Assert.That(settings.Timeout).IsEqualTo(500);
    }

    #endregion
}
