namespace Kurrent.Surge.Codecs.Avro.Tests;

[Category("Codecs")]
[Timeout(10_000)]
public class AvroFixedValueTests {
    sealed class Sixteen() : AvroFixedValue(16);

    [Test]
    public async ValueTask carries_a_value_of_the_declared_size() {
        // Arrange
        var expected = new byte[16];
        expected[0] = 0xCA;
        expected[15] = 0xFE;

        // Act
        var @fixed = new Sixteen { Value = expected };

        // Assert
        await Assert.That(@fixed.Value).IsEquivalentTo(expected);
        await Assert.That(@fixed.Size).IsEqualTo(16);
    }

    [Test]
    public async ValueTask defaults_to_the_declared_size_before_any_value_is_assigned() {
        // Act
        var @fixed = new Sixteen();

        // Assert
        await Assert.That(@fixed.Value.Length).IsEqualTo(16);
        await Assert.That(@fixed.Size).IsEqualTo(16);
    }

    [Test]
    [Arguments(3)]
    [Arguments(17)]
    [Arguments(0)]
    public async ValueTask rejects_a_value_that_is_not_the_declared_size(int length) {
        // Arrange — the encoder writes as many bytes as the array holds while the decoder reads the
        // count the schema declares, so a wrong length desynchronises every field written after it.
        var wrong = new byte[length];

        // Act
        var act = () => new Sixteen { Value = wrong };

        // Assert
        await Assert.That(act).Throws<ArgumentException>();
    }
}
