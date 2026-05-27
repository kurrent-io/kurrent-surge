using Kurrent.Client.Streams;
using Serilog;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class SchemaFilterTests {
    readonly RecordFaker _faker = new();

    #region ->> Test Case Generators <<-

    public class ValidInputCases : TestCaseDataGenerator<(string Input, string SchemaName, string IndexName)> {
        protected override IEnumerable<TestCase<(string Input, string SchemaName, string IndexName)>> Data() {
            yield return new(("OrderCreated",         "OrderCreated", "$idx-et-OrderCreated"), DisplayName: "plain name");
            yield return new(("$et-OrderCreated",     "OrderCreated", "$idx-et-OrderCreated"), DisplayName: "$et- prefix");
            yield return new(("$idx-et-OrderCreated", "OrderCreated", "$idx-et-OrderCreated"), DisplayName: "$idx-et- prefix");
            yield return new(("$ET-OrderCreated",     "OrderCreated", "$idx-et-OrderCreated"), DisplayName: "uppercase $ET- prefix");
            yield return new(("$IDX-ET-OrderCreated", "OrderCreated", "$idx-et-OrderCreated"), DisplayName: "uppercase $IDX-ET- prefix");
        }
    }

    public class InvalidInputCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("",            DisplayName: "empty");
            yield return new(" ",           DisplayName: "whitespace");
            yield return new("$ce-orders",  DisplayName: "wrong prefix $ce-");
            yield return new("order items", DisplayName: "whitespace in name");
            yield return new("$idx-et-",    DisplayName: "empty after $idx-et-");
        }
    }

    #endregion

    #region ->> Parse - Valid Inputs <<-

    [Test]
    [ValidInputCases]
    public async ValueTask parse_returns_expected_properties(string input, string expectedSchemaName, string expectedIndexName) {
        // Act
        var filter = SchemaFilter.Parse(input);

        // Assert
        await Assert.That(filter.SchemaName).IsEqualTo(expectedSchemaName);
        await Assert.That(filter.IndexName).IsEqualTo(expectedIndexName);
    }

    [Test]
    public async ValueTask parse_preserves_expression_as_raw_input() {
        var filter = SchemaFilter.Parse("$et-OrderCreated");

        await Assert.That(filter.Expression).IsEqualTo("$et-OrderCreated");
    }

    [Test]
    [ValidInputCases]
    public async ValueTask try_parse_returns_true_and_expected_properties(string input, string expectedSchemaName, string expectedIndexName) {
        // Act
        var success = SchemaFilter.TryParse(input, out var filter, out _);

        // Assert
        await Assert.That(success).IsTrue();
        await Assert.That(filter).IsNotNull();
        await Assert.That(filter!.SchemaName).IsEqualTo(expectedSchemaName);
        await Assert.That(filter.IndexName).IsEqualTo(expectedIndexName);
    }

    #endregion

    #region ->> Parse - Invalid Inputs <<-

    [Test]
    [InvalidInputCases]
    public async ValueTask parse_throws_on_invalid_input(string input) {
        await Assert.That(() => SchemaFilter.Parse(input)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [InvalidInputCases]
    public async ValueTask try_parse_returns_false_on_invalid_input(string input) {
        // Act
        var result = SchemaFilter.TryParse(input, out var filter, out var error);

        Log.Information("TryParse rejected '{Input}': {Error}", input, error);

        // Assert
        await Assert.That(result).IsFalse();
        await Assert.That(filter).IsNull();
    }

    #endregion

    #region ->> IsMatch <<-

    [Test]
    public async ValueTask is_match_returns_true_for_matching_schema_name() {
        var filter = SchemaFilter.Parse("OrderCreated");
        var record = _faker.With(schemaName: "OrderCreated");

        await Assert.That(filter.IsMatch(record)).IsTrue();
    }

    public class CaseInsensitiveSchemaCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("ordercreated", DisplayName: "lower case");
            yield return new("ORDERCREATED", DisplayName: "upper case");
            yield return new("OrderCreated", DisplayName: "pascal case");
        }
    }

    [Test]
    [CaseInsensitiveSchemaCases]
    public async ValueTask is_match_is_case_insensitive(string schemaName) {
        var filter = SchemaFilter.Parse("OrderCreated");

        await Assert.That(filter.IsMatch(_faker.With(schemaName: schemaName))).IsTrue();
    }

    [Test]
    public async ValueTask is_match_returns_false_for_different_schema_name() {
        var filter = SchemaFilter.Parse("OrderCreated");
        var record = _faker.With(schemaName: "OrderShipped");

        await Assert.That(filter.IsMatch(record)).IsFalse();
    }

    #endregion
}
