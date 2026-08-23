using Kurrent.Surge.Client.Streams;
using Serilog;

namespace Kurrent.Surge.Tests.Filters;

[Category("Filters")]
public class CategoryFilterTests {
    readonly RecordFaker _faker = new();

    #region ->> Test Case Generators <<-

    public class ValidInputCases : TestCaseDataGenerator<(string Input, string CategoryName, string Expression, string IndexName)> {
        protected override IEnumerable<TestCase<(string Input, string CategoryName, string Expression, string IndexName)>> Data() {
            yield return new(("orders",         "orders", "orders",         "$idx-ce-orders"), DisplayName: "plain name");
            yield return new(("$ce-orders",     "orders", "$ce-orders",     "$idx-ce-orders"), DisplayName: "$ce- prefix");
            yield return new(("$idx-ce-orders", "orders", "$idx-ce-orders", "$idx-ce-orders"), DisplayName: "$idx-ce- prefix");
            yield return new((" orders ",       "orders", "orders",         "$idx-ce-orders"), DisplayName: "trimmed whitespace");
            yield return new(("$CE-orders",     "orders", "$CE-orders",     "$idx-ce-orders"), DisplayName: "uppercase $CE- prefix");
            yield return new(("$IDX-CE-orders", "orders", "$IDX-CE-orders", "$idx-ce-orders"), DisplayName: "uppercase $IDX-CE- prefix");
        }
    }

    public class InvalidInputCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("",                  DisplayName: "empty");
            yield return new(" ",                 DisplayName: "whitespace");
            yield return new("$et-OrderCreated",  DisplayName: "wrong prefix $et-");
            yield return new("$idx-et-OrderCreated", DisplayName: "wrong prefix $idx-et-");
            yield return new("order items",       DisplayName: "whitespace in name");
            yield return new("$idx-ce-",          DisplayName: "empty after $idx-ce-");
            yield return new("$ce-",              DisplayName: "empty after $ce-");
            yield return new("$bad-prefix",       DisplayName: "unknown $ prefix");
        }
    }

    #endregion

    #region ->> Parse - Valid Inputs <<-

    [Test]
    [ValidInputCases]
    public async ValueTask parse_returns_expected_properties(string input, string expectedCategoryName, string expectedExpression, string expectedIndexName) {
        // Act
        var filter = CategoryFilter.Parse(input);

        // Assert
        await Assert.That(filter.CategoryName).IsEqualTo(expectedCategoryName);
        await Assert.That(filter.Expression).IsEqualTo(expectedExpression);
        await Assert.That(filter.IndexName).IsEqualTo(expectedIndexName);
    }

    [Test]
    [ValidInputCases]
    public async ValueTask try_parse_returns_true_and_expected_properties(string input, string expectedCategoryName, string expectedExpression, string expectedIndexName) {
        // Act
        var success = CategoryFilter.TryParse(input, out var filter, out _);

        // Assert
        await Assert.That(success).IsTrue();
        await Assert.That(filter).IsNotNull();
        await Assert.That(filter!.CategoryName).IsEqualTo(expectedCategoryName);
        await Assert.That(filter.Expression).IsEqualTo(expectedExpression);
        await Assert.That(filter.IndexName).IsEqualTo(expectedIndexName);
    }

    #endregion

    #region ->> Parse - Invalid Inputs <<-

    [Test]
    [InvalidInputCases]
    public async ValueTask parse_throws_on_invalid_input(string input) {
        await Assert.That(() => CategoryFilter.Parse(input)).Throws<ArgumentException>();
    }

    [Test]
    [InvalidInputCases]
    public async ValueTask try_parse_returns_false_on_invalid_input(string input) {
        // Act
        var success = CategoryFilter.TryParse(input, out var filter, out var error);

        Log.Information("TryParse rejected '{Input}': {Error}", input, error);

        // Assert
        await Assert.That(success).IsFalse();
        await Assert.That(filter).IsNull();
    }

    #endregion

    #region ->> IsMatch <<-

    [Test]
    public async ValueTask is_match_returns_true_for_record_in_same_category() {
        var filter   = CategoryFilter.Parse("Orders");
        var matching = _faker.With("Orders-D0332EB7480643E481BF3205296958FD");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
    }

    [Test]
    public async ValueTask is_match_returns_false_for_record_in_different_category() {
        var filter      = CategoryFilter.Parse("orders");
        var nonMatching = _faker.With("Payments-A1B2C3D4E5F6789012345678ABCDEF01");

        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    // A category compares ordinally, matching how an expression compares `category = '...'`.
    public class DifferentlyCasedCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("Orders-001",  DisplayName: "title case");
            yield return new("ORDERS-001",  DisplayName: "upper case");
        }
    }

    [Test]
    [DifferentlyCasedCases]
    public async ValueTask is_match_is_case_sensitive(string stream) {
        var filter = CategoryFilter.Parse("orders");

        await Assert.That(filter.IsMatch(_faker.With(stream))).IsFalse();
    }

    [Test]
    public async ValueTask is_match_matches_an_identically_cased_category() {
        var filter = CategoryFilter.Parse("orders");

        await Assert.That(filter.IsMatch(_faker.With("orders-001"))).IsTrue();
    }

    [Test]
    public async ValueTask is_match_via_ce_prefix_matches_same_category() {
        var filter   = CategoryFilter.Parse("$ce-Orders");
        var matching = _faker.With("Orders-D0332EB7480643E481BF3205296958FD");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
    }

    [Test]
    public async ValueTask is_match_via_idx_ce_prefix_matches_same_category() {
        var filter   = CategoryFilter.Parse("$idx-ce-Orders");
        var matching = _faker.With("Orders-D0332EB7480643E481BF3205296958FD");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
    }

    #endregion
}
