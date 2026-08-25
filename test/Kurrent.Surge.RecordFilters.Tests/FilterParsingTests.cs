using Kurrent.Surge.RecordFilters.Tests.Targets;

namespace Kurrent.Surge.RecordFilters.Tests;

// Rejected expressions have no SQL counterpart to differential-test, unlike DuckDbDifferentialTests.
[Category("RecordFilters")]
public class FilterParsingTests {
    #region ->> Test Case Generators <<-

    public class RejectedExpressionCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("stream =",                       DisplayName: "missing value");
            yield return new("= 'orders'",                     DisplayName: "missing column");
            yield return new("stream = 'orders",               DisplayName: "unclosed string");
            yield return new("(stream = 'orders'",             DisplayName: "unclosed parenthesis");
            yield return new("stream = 'orders' AND",          DisplayName: "missing AND operand");
            yield return new("stream = 'orders' OR",           DisplayName: "missing OR operand");
            yield return new("stream === 'orders'",            DisplayName: "invalid operator");
            yield return new("stream IN ()",                   DisplayName: "empty IN list");
            yield return new("amount BETWEEN 100 200",         DisplayName: "BETWEEN without AND");
            yield return new("stream = 'orders' TOTALLY 'x'",  DisplayName: "trailing garbage");
            yield return new("stream IS",                      DisplayName: "IS without NULL");
            yield return new("stream NOT",                     DisplayName: "NOT without operator");
            yield return new("data->'tags' IS NOT NULL",       DisplayName: "single arrow unsupported");
        }
    }

    public class ScalarFieldAccessCases : TestCaseDataGenerator<(string Expression, string Field, string Column)> {
        protected override IEnumerable<TestCase<(string Expression, string Field, string Column)>> Data() {
            yield return new(("stream.region = 'a'",     "region", "stream"),          "dotted path");
            yield return new(("stream['region'] = 'a'",  "region", "stream"),          "bracket key");
            yield return new(("stream->>'region' = 'a'", "region", "stream"),          "arrow key");
            yield return new(("revision.region = 1",     "region", "stream_revision"), "an alias reports its canonical column");
        }
    }

    public class BlankInputCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("",      DisplayName: "empty");
            yield return new("   ",   DisplayName: "spaces");
            yield return new("\t\n",  DisplayName: "tab and newline");
        }
    }

    #endregion

    #region ->> Rejection <<-

    // DuckDB refuses these when it binds the query, so they never reach a record. Previously they
    // compared the two ToString() forms and answered arbitrarily.
    public class IncomparableTypeCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("stream = 123",                 DisplayName: "number against a text column");
            yield return new("stream > 5",                   DisplayName: "ordering a text column by a number");
            yield return new("stream = true",                DisplayName: "boolean against a text column");
            yield return new("stream IN ('a', 2)",           DisplayName: "a number inside an IN list");
            yield return new("stream BETWEEN 1 AND 2",       DisplayName: "a numeric range over text");
        }
    }

    [Test]
    [IncomparableTypeCases]
    public async ValueTask rejects_a_literal_that_cannot_compare(string expression) {
        var parsed = OrderSchema.Default.TryParse(expression, out _, out var error);

        await Assert.That(parsed).IsFalse();
        await Assert.That(error!.ErrorMessage).Contains("an explicit cast is required");
    }

    // A string literal binds against a numeric column but has to convert, and the literal is constant
    // so the failure is settled at parse time rather than once per record.
    public class UnconvertibleLiteralCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("created_at < 'abc'",           DisplayName: "text against a timestamp column");
            yield return new("created_at = 'yesterday'",     DisplayName: "a word against a timestamp column");
            yield return new("stream_revision > 'abc'",      DisplayName: "text against a numeric column");
            yield return new("amount BETWEEN 'a' AND 'b'",   DisplayName: "a text range over a numeric column");
        }
    }

    [Test]
    [UnconvertibleLiteralCases]
    public async ValueTask rejects_a_literal_that_cannot_convert(string expression) {
        var parsed = OrderSchema.Default.TryParse(expression, out _, out var error);

        await Assert.That(parsed).IsFalse();
        await Assert.That(error!.ErrorMessage).Contains("Conversion Error");
    }

    [Test]
    [RejectedExpressionCases]
    public async ValueTask rejects_malformed_expression(string expression) {
        // Arrange
        const string expectedPrefix = "Error parsing filter:";

        // Act
        var parsed = OrderSchema.Default.TryParse(expression, out var filter, out var error);

        // Assert
        await Assert.That(parsed).IsFalse();
        await Assert.That(filter).IsNull();
        await Assert.That(error!.ErrorMessage).Contains(expectedPrefix);
    }

    [Test]
    [ScalarFieldAccessCases]
    public async ValueTask rejects_a_field_read_from_a_scalar_column(string expression, string field, string column) {
        // Arrange
        var expectedMessage =
            $"Error parsing filter: cannot extract field '{field}' from column \"{column}\" because it is not a map or json column";

        // Act
        var parsed = OrderSchema.Default.TryParse(expression, out var filter, out var error);

        // Assert
        await Assert.That(parsed).IsFalse();
        await Assert.That(filter).IsNull();
        await Assert.That(error!.ErrorMessage).IsEqualTo(expectedMessage);
    }

    [Test]
    [BlankInputCases]
    public async ValueTask rejects_blank_input(string expression) {
        // Arrange
        const string expectedMessage = "Error parsing filter: empty expression";

        // Act
        var parsed = OrderSchema.Default.TryParse(expression, out var filter, out var error);

        // Assert
        await Assert.That(parsed).IsFalse();
        await Assert.That(filter).IsNull();
        await Assert.That(error!.ErrorMessage).IsEqualTo(expectedMessage);
        await Assert.That(error.NearToken).IsNull();
    }

    [Test]
    public async ValueTask reports_the_token_parsing_stopped_at() {
        // Arrange
        var expression        = "stream = 'orders' TOTALLY 'invalid'";
        var expectedNearToken = "TOTALLY";

        // Act
        OrderSchema.Default.TryParse(expression, out _, out var error);

        // Assert
        await Assert.That(error!.NearToken).IsEqualTo(expectedNearToken);
        await Assert.That(error.ErrorMessage).Contains(expectedNearToken);
        await Assert.That(error.Position).IsEqualTo(expression.IndexOf(expectedNearToken, StringComparison.Ordinal));
    }

    [Test]
    public async ValueTask throws_a_format_exception_carrying_the_error() {
        // Arrange
        var expression = "stream =";

        // Act & Assert
        var thrown = await Assert.That(() => OrderSchema.Default.Parse(expression)).Throws<FilterFormatException>();

        await Assert.That(thrown!.ParseError.ErrorMessage).Contains("syntax error");
    }

    #endregion

    #region ->> Parser Surface <<-

    [Test]
    public async ValueTask accepts_readonly_span_input() {
        // Arrange — the schema surface takes a string; the parser itself accepts a span
        var expression = "stream = 'orders-1'";

        // Act
        var parsed = FilterParser.TryParse(expression.AsSpan(), out var tree, out var error);

        // Assert
        await Assert.That(parsed).IsTrue();
        await Assert.That(tree).IsNotNull();
        await Assert.That(error).IsNull();
    }

    #endregion
}
