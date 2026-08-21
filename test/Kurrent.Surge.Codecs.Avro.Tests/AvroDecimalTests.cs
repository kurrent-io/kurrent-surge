using System.Numerics;
using Kurrent.Surge.Codecs.Avro;

namespace Kurrent.Surge.Codecs.Avro.Tests;

/// <summary>
/// Covers <see cref="Kurrent.Surge.Codecs.Avro.AvroDecimal"/>, the arbitrary-precision decimal the Avro
/// <c>decimal</c> logical type stores.
/// </summary>
/// <remarks>
/// The value it exists to carry is one <see cref="decimal"/> cannot: Avro places no bound on precision.
/// So the tests that matter are the ones past 96 bits, where a naive implementation silently overflows.
/// </remarks>
[Category("Codecs")]
[Timeout(10_000)]
public class AvroDecimalTests {
    static readonly Faker Faker = new();

    #region ->> Test Case Generators <<-

    public sealed class DecimalCases : TestCaseDataGenerator<decimal> {
        protected override IEnumerable<TestCase<decimal>> Data() {
            yield return new(0m, DisplayName: "zero");
            yield return new(1m, DisplayName: "one");
            yield return new(-1m, DisplayName: "minus one");
            yield return new(1234.56m, DisplayName: "two decimal places");
            yield return new(-1234.56m, DisplayName: "negative, two decimal places");
            yield return new(0.0000000001m, DisplayName: "ten decimal places");
            yield return new(decimal.MaxValue, DisplayName: "decimal.MaxValue");
            yield return new(decimal.MinValue, DisplayName: "decimal.MinValue");
        }
    }

    #endregion // Test Case Generators

    [Test]
    [DecimalCases]
    public async ValueTask round_trips_a_decimal(decimal value) {
        // Arrange & Act
        var actual = new AvroDecimal(value).ToDecimal();

        // Assert
        await Assert.That(actual).IsEqualTo(value);
    }

    [Test]
    public async ValueTask keeps_the_scale_a_trailing_zero_implies() {
        // Arrange — 1.50 and 1.5 are the same number and different Avro values: they hold different
        // unscaled values and encode to different bytes.
        var padded = new AvroDecimal(1.50m);
        var bare   = new AvroDecimal(1.5m);

        // Act & Assert
        await Assert.That(padded.Scale).IsEqualTo(2);
        await Assert.That(bare.Scale).IsEqualTo(1);
        await Assert.That(padded).IsNotEqualTo(bare);
        await Assert.That(padded.CompareTo(bare)).IsEqualTo(0);
    }

    [Test]
    public async ValueTask compares_values_wider_than_a_decimal_without_overflowing() {
        // Arrange — this is the case that breaks an implementation narrowing through decimal. The
        // unscaled values are past 96 bits, so decimal cannot hold either of them.
        var big     = BigInteger.Pow(10, 40);
        var bigger  = big + BigInteger.One;

        var left  = new AvroDecimal(big, 2);
        var right = new AvroDecimal(bigger, 2);

        // Act & Assert
        await Assert.That(left.CompareTo(right)).IsLessThan(0);
        await Assert.That(right.CompareTo(left)).IsGreaterThan(0);
        await Assert.That(left < right).IsTrue();
    }

    [Test]
    public async ValueTask compares_across_different_scales() {
        // Arrange — 1.5 against 1.50, then against 1.51, so alignment has to happen before comparison
        // rather than the raw unscaled values being compared.
        var oneAndAHalf = new AvroDecimal(15, 1);
        var same        = new AvroDecimal(150, 2);
        var larger      = new AvroDecimal(151, 2);

        // Act & Assert
        await Assert.That(oneAndAHalf.CompareTo(same)).IsEqualTo(0);
        await Assert.That(oneAndAHalf.CompareTo(larger)).IsLessThan(0);
    }

    [Test]
    public async ValueTask renders_a_value_too_wide_for_a_decimal() {
        // Arrange — ToString goes through the unscaled digits, not through decimal, so a value decimal
        // cannot hold still prints exactly.
        var value = new AvroDecimal(BigInteger.Parse("123456789012345678901234567890123456789"), 5);

        // Act
        var actual = value.ToString();

        // Assert
        await Assert.That(actual).IsEqualTo("1234567890123456789012345678901234.56789");
    }

    [Test]
    public async ValueTask renders_a_value_smaller_than_its_scale() {
        // Arrange — fewer digits than the scale needs a leading zero and padding.
        var value = new AvroDecimal(new BigInteger(5), 3);

        // Act & Assert
        await Assert.That(value.ToString()).IsEqualTo("0.005");
        await Assert.That(new AvroDecimal(new BigInteger(-5), 3).ToString()).IsEqualTo("-0.005");
    }

    [Test]
    public async ValueTask rejects_a_negative_scale() {
        // Arrange — the Avro specification defines scale as a non-negative count of digits.
        // Act & Assert
        await Assert.That(() => new AvroDecimal(BigInteger.One, -1)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async ValueTask throws_rather_than_wrapping_when_a_value_will_not_fit_a_decimal() {
        // Arrange — silently returning a wrapped value is the failure this type exists to prevent.
        var value = new AvroDecimal(BigInteger.Pow(10, 40), 2);

        // Act & Assert
        await Assert.That(() => value.ToDecimal()).Throws<OverflowException>();
    }

    [Test]
    public async ValueTask agrees_with_apache_on_the_unscaled_value_and_scale() {
        // Arrange — the wire encoding is written from these two members, so agreeing on them is what
        // makes ours a drop-in for Apache's on the encode path.
        var value = decimal.Round(Faker.Random.Decimal(1, 9999), 2);

        var ours   = new AvroDecimal(value);
        var apache = new global::Avro.AvroDecimal(value);

        // Act & Assert
        await Assert.That(ours.UnscaledValue).IsEqualTo(apache.UnscaledValue);
        await Assert.That(ours.Scale).IsEqualTo(apache.Scale);
    }
}
