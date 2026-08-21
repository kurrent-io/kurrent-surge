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

    public class BlankInputCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("",      DisplayName: "empty");
            yield return new("   ",   DisplayName: "spaces");
            yield return new("\t\n",  DisplayName: "tab and newline");
        }
    }

    #endregion

    #region ->> Rejection <<-

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
