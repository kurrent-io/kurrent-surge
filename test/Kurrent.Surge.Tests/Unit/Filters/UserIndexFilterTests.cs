using Kurrent.Client.Streams;
using Serilog;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class UserIndexFilterTests {
    readonly RecordFaker _faker = new();

    #region ->> Test Case Generators <<-

    public class ValidInputCases : TestCaseDataGenerator<(string Input, string IndexName)> {
        protected override IEnumerable<TestCase<(string Input, string IndexName)>> Data() {
            yield return new(("orders-by-country",              "$idx-user-orders-by-country"), DisplayName: "plain name");
            yield return new(("$idx-user-orders-by-country",    "$idx-user-orders-by-country"), DisplayName: "$idx-user- prefix");
            yield return new(("my_index",                       "$idx-user-my_index"),          DisplayName: "underscores");
            yield return new(("abc123",                         "$idx-user-abc123"),            DisplayName: "alphanumeric");
        }
    }

    public class InvalidInputCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("",            DisplayName: "empty");
            yield return new(" ",           DisplayName: "whitespace");
            yield return new("MyIndex",     DisplayName: "uppercase");
            yield return new("MYINDEX",     DisplayName: "all uppercase");
            yield return new("my index",    DisplayName: "whitespace in name");
            yield return new("my.index",    DisplayName: "dots");
            yield return new("$idx-user-",  DisplayName: "empty after prefix");
        }
    }

    #endregion

    #region ->> Parse - Valid Inputs <<-

    [Test]
    [ValidInputCases]
    public async ValueTask parse_returns_expected_index_name(string input, string expectedIndexName) {
        // Act
        var filter = UserIndexFilter.Parse(input);

        // Assert
        await Assert.That(filter.IndexName).IsEqualTo(expectedIndexName);
    }

    [Test]
    public async ValueTask parse_preserves_expression_as_raw_input() {
        var filter = UserIndexFilter.Parse("orders-by-country");

        await Assert.That(filter.Expression).IsEqualTo("orders-by-country");
    }

    [Test]
    public async ValueTask parse_accepts_name_at_max_length() {
        var name   = new string('a', 1000);
        var filter = UserIndexFilter.Parse(name);

        await Assert.That(filter.IndexName).IsEqualTo($"$idx-user-{name}");
    }

    [Test]
    [ValidInputCases]
    public async ValueTask try_parse_returns_true_and_expected_properties(string input, string expectedIndexName) {
        // Act
        var success = UserIndexFilter.TryParse(input, out var filter, out _);

        // Assert
        await Assert.That(success).IsTrue();
        await Assert.That(filter).IsNotNull();
        await Assert.That(filter!.IndexName).IsEqualTo(expectedIndexName);
    }

    #endregion

    #region ->> Parse - Invalid Inputs <<-

    [Test]
    [InvalidInputCases]
    public async ValueTask parse_throws_on_invalid_input(string input) {
        await Assert.That(() => UserIndexFilter.Parse(input)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async ValueTask parse_throws_when_name_exceeds_max_length() {
        var name = new string('a', 1001);

        await Assert.That(() => UserIndexFilter.Parse(name)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [InvalidInputCases]
    public async ValueTask try_parse_returns_false_on_invalid_input(string input) {
        // Act
        var result = UserIndexFilter.TryParse(input, out var filter, out var error);

        Log.Information("TryParse rejected '{Input}': {Error}", input, error);

        // Assert
        await Assert.That(result).IsFalse();
        await Assert.That(filter).IsNull();
    }

    [Test]
    public async ValueTask try_parse_returns_false_when_name_exceeds_max_length() {
        var name   = new string('a', 1001);
        var result = UserIndexFilter.TryParse(name, out var filter, out var error);

        Log.Information("TryParse rejected over-length name: {Error}", error);

        await Assert.That(result).IsFalse();
        await Assert.That(filter).IsNull();
    }

    #endregion

    #region ->> IsMatch <<-

    [Test]
    public async ValueTask is_match_always_returns_true() {
        var filter  = UserIndexFilter.Parse("orders-by-country");
        var record1 = _faker.Generate();
        var record2 = _faker.Generate();

        await Assert.That(filter.IsMatch(record1)).IsTrue();
        await Assert.That(filter.IsMatch(record2)).IsTrue();
    }

    #endregion
}
