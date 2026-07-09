using Kurrent.Surge.Client.Streams;
using Serilog;

namespace Kurrent.Surge.Tests.Filters;

[Category("Filters")]
public class StreamRegexFilterTests {
    readonly RecordFaker _faker = new();

    const string OrdersStream   = "Orders-D0332EB7480643E481BF3205296958FD";
    const string PaymentsStream = "Payments-A1B2C3D4E5F6789012345678ABCDEF01";

    #region ->> Test Case Generators <<-

    public class ValidPatternCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("^Orders-.*$",                 DisplayName: "category wildcard");
            yield return new("^Orders-.$",                  DisplayName: "single char wildcard");
            yield return new("(?i)^ORDERS-ABC123$",         DisplayName: "case insensitive");
            yield return new("^(Orders-001|Payments-002)$", DisplayName: "alternation");
        }
    }

    public class InvalidPatternCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("",                       DisplayName: "empty");
            yield return new(" ",                      DisplayName: "whitespace");
            yield return new(@"^\$idx-ce-orders$",     DisplayName: "category index pattern");
            yield return new(@"^\$idx-et-.+$",         DisplayName: "schema index pattern");
            yield return new(@"[invalid",              DisplayName: "invalid regex syntax");
        }
    }

    public class CaseInsensitiveCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("ORDERS-ABC123", DisplayName: "upper case");
            yield return new("orders-ABC123", DisplayName: "lower case");
            yield return new("Orders-ABC123", DisplayName: "title case");
        }
    }

    #endregion

    #region ->> Parse <<-

    [Test]
    [ValidPatternCases]
    public async ValueTask parse_returns_regex_filter(string pattern) {
        // Act
        var filter = StreamRegexFilter.Parse(pattern);

        // Assert
        await Assert.That(filter.Expression).IsEqualTo(pattern);
    }

    [Test]
    [InvalidPatternCases]
    public async ValueTask parse_throws_on_invalid_pattern(string pattern) {
        await Assert.That(() => StreamRegexFilter.Parse(pattern)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [InvalidPatternCases]
    public async ValueTask try_parse_returns_false_on_invalid_pattern(string pattern) {
        // Act
        var result = StreamRegexFilter.TryParse(pattern, out var filter, out var error);

        Log.Information("TryParse rejected '{Pattern}': {Error}", pattern, error);

        // Assert
        await Assert.That(result).IsFalse();
        await Assert.That(filter).IsNull();
    }

    #endregion

    #region ->> IsMatch <<-

    [Test]
    public async ValueTask is_match_matches_category_prefix() {
        var filter = StreamRegexFilter.Parse("^Orders-.*$");

        await Assert.That(filter.IsMatch(_faker.With(OrdersStream))).IsTrue();
        await Assert.That(filter.IsMatch(_faker.With(PaymentsStream))).IsFalse();
    }

    [Test]
    [CaseInsensitiveCases]
    public async ValueTask is_match_case_insensitive_matches(string stream) {
        var filter = StreamRegexFilter.Parse("(?i)^ORDERS-ABC123$");

        await Assert.That(filter.IsMatch(_faker.With(stream))).IsTrue();
    }

    [Test]
    public async ValueTask is_match_case_insensitive_rejects_non_matching() {
        var filter = StreamRegexFilter.Parse("(?i)^ORDERS-ABC123$");

        await Assert.That(filter.IsMatch(_faker.With("Payments-ABC123"))).IsFalse();
    }

    #endregion
}
