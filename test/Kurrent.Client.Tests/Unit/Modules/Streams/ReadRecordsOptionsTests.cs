using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Modules.Streams;

[Category("Streams")]
public class ReadRecordsOptionsTests {
    [Test]
    public async ValueTask default_options_have_expected_defaults() {
        // Arrange & Act
        var options = new ReadRecordsOptions();

        // Assert
        await Assert.That(options.Start).IsEqualTo(RecordPosition.Latest);
        await Assert.That(options.Limit).IsEqualTo(long.MaxValue);
        await Assert.That(options.Direction).IsEqualTo(ReadDirection.Forwards);
        await Assert.That(options.SkipDecoding).IsFalse();
        await Assert.That(options.ResolveLinks).IsTrue();
        await Assert.That(options.Filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async ValueTask default_options_are_valid() {
        // Arrange
        var options = new ReadRecordsOptions();

        // Act
        var result = options.Validate();

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask limit_below_one_is_invalid() {
        // Arrange
        var options = new ReadRecordsOptions { Limit = 0 };

        // Act
        var result = options.Validate();

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
    }

    [Test]
    public void ensure_valid_throws_when_limit_below_one() {
        // Arrange
        var options = new ReadRecordsOptions { Limit = 0 };

        // Act & Assert
        Assert.Throws<Exception>(() => options.EnsureValid());
    }

    [Test]
    public async ValueTask unset_start_position_is_invalid() {
        // Arrange
        var options = new ReadRecordsOptions { Start = LogPosition.Unset };

        // Act
        var result = options.Validate();

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
    }
}
