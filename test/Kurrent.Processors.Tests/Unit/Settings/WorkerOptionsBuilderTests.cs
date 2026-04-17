using Kurrent.Processors.Options;

namespace Kurrent.Processors.Tests.Unit.Settings;

[Category("Processors")]
public class WorkerOptionsBuilderTests {
    #region Default Values

    [Test]
    public async ValueTask defaults_buffer_capacity_is_1000() {
        // Arrange
        // Act
        var settings = new WorkerOptions.Builder().Build();

        // Assert
        await Assert.That(settings.BufferCapacity).IsEqualTo(1000);
    }

    [Test]
    public async ValueTask defaults_skip_decoding_is_false() {
        // Arrange
        // Act
        var settings = new WorkerOptions.Builder().Build();

        // Assert
        await Assert.That(settings.SkipDecoding).IsFalse();
    }

    #endregion

    #region BufferCapacity

    [Test]
    public async ValueTask buffer_capacity_sets_value() {
        // Arrange
        // Act
        var settings = new WorkerOptions.Builder()
            .BufferCapacity(5000)
            .Build();

        // Assert
        await Assert.That(settings.BufferCapacity).IsEqualTo(5000);
    }

    #endregion

    #region SkipDecoding

    [Test]
    public async ValueTask skip_decoding_sets_true_when_called_with_true() {
        // Arrange
        // Act
        var settings = new WorkerOptions.Builder()
            .SkipDecoding(true)
            .Build();

        // Assert
        await Assert.That(settings.SkipDecoding).IsTrue();
    }

    [Test]
    public async ValueTask skip_decoding_defaults_to_true_when_called_without_argument() {
        // Arrange
        // Act
        var settings = new WorkerOptions.Builder()
            .SkipDecoding()
            .Build();

        // Assert
        await Assert.That(settings.SkipDecoding).IsTrue();
    }

    [Test]
    public async ValueTask skip_decoding_sets_false_when_called_with_false() {
        // Arrange
        // Act
        var settings = new WorkerOptions.Builder()
            .SkipDecoding(false)
            .Build();

        // Assert
        await Assert.That(settings.SkipDecoding).IsFalse();
    }

    #endregion

    #region Method Chaining

    [Test]
    public async ValueTask chained_calls_apply_all_values() {
        // Arrange
        // Act
        var settings = new WorkerOptions.Builder()
            .BufferCapacity(2000)
            .SkipDecoding(true)
            .Build();

        // Assert
        await Assert.That(settings.BufferCapacity).IsEqualTo(2000);
        await Assert.That(settings.SkipDecoding).IsTrue();
    }

    #endregion
}
