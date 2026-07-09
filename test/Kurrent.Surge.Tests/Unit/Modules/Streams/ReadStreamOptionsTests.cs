using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Tests.Modules.Streams;

// Stream-name rejection ($all / unset) used to live on the request validator. It now lives on the
// StreamsClient.ReadStream(stream, options, ct) method guard, so it is covered by the integration
// read tests rather than these options unit tests.
[Category("Streams")]
public class ReadStreamOptionsTests {
    [Test]
    public async ValueTask default_options_have_expected_defaults() {
        // Arrange & Act
        var options = new ReadStreamOptions();

        // Assert
        await Assert.That(options.Start).IsEqualTo(Revision.Unset);
        await Assert.That(options.Limit).IsEqualTo(long.MaxValue);
        await Assert.That(options.Direction).IsEqualTo(ReadDirection.Forwards);
        await Assert.That(options.SkipDecoding).IsFalse();
        await Assert.That(options.ResolveLinks).IsTrue();
    }

    [Test]
    public async ValueTask default_options_are_valid() {
        // Arrange
        var options = new ReadStreamOptions();

        // Act
        var result = options.Validate();

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask limit_below_one_is_invalid() {
        // Arrange
        var options = new ReadStreamOptions { Limit = 0 };

        // Act
        var result = options.Validate();

        // Assert
        await Assert.That(result.IsInvalid).IsTrue();
    }

    [Test]
    public async ValueTask max_start_revision_reading_forwards_is_valid() {
        // Arrange — forwards from the end (Revision.Max) reads nothing: an empty success, not an
        // error. The read path short-circuits it, mirroring $all's End + Forwards behavior.
        var options = new ReadStreamOptions { Start = Revision.Max, Direction = ReadDirection.Forwards };

        // Act
        var result = options.Validate();

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask max_start_revision_reading_backwards_is_valid() {
        // Arrange
        var options = new ReadStreamOptions { Start = Revision.Max, Direction = ReadDirection.Backwards };

        // Act
        var result = options.Validate();

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }
}
