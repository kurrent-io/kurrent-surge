using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
[Skip("Filter tests are currently disabled pending implementation of the full filter evaluation engine.")]
public class FilterTokenizerTests {
    [Test]
    public void tokenizes_simple_expressions_correctly() {
        // Test basic expression parsing through the main API
        var result = FilterExpression.Create("stream = 'orders'");

        result.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void tokenizes_complex_expressions_with_all_operators() {
        var expressions = new[] {
            "stream = 'orders' AND schema.name != 'SystemEvent'",
            "stream <> 'test' OR metadata.amount > 100",
            "stream ~ 'order.*' AND metadata.userId IS NOT NULL",
            "stream IN ('orders', 'payments') AND timestamp >= '2024-01-01T00:00:00Z'",
            "schema.name NOT IN ('SystemEvent', 'TestEvent') AND stream ~* 'Order.*'",
            "stream !~ 'test.*' OR metadata.score <= 95"
        };

        foreach (var expression in expressions) {
            var result = FilterExpression.Create(expression);
            result.IsSuccess.ShouldBeTrue($"Failed to tokenize: {expression}");
        }
    }

    [Test]
    public void handles_string_escaping_correctly() {
        var result = FilterExpression.Create("stream = 'user\\'s-orders'");

        result.IsSuccess.ShouldBeTrue();
    }

    [Test]
    public void handles_numeric_values_correctly() {
        var expressions = new[] {
            "metadata.amount = 42",
            "metadata.price = 123.45",
            "metadata.count >= 100"
        };

        foreach (var expression in expressions) {
            var result = FilterExpression.Create(expression);
            result.IsSuccess.ShouldBeTrue($"Failed to parse numeric expression: {expression}");
        }
    }

    [Test]
    public void handles_datetime_literals() {
        var expressions = new[] {
            "timestamp = '2024-01-15T10:30:00Z'",
            "timestamp >= '2024-01-01T00:00:00+02:00'",
            "timestamp < '2024-12-31'"
        };

        foreach (var expression in expressions) {
            var result = FilterExpression.Create(expression);
            result.IsSuccess.ShouldBeTrue($"Failed to parse datetime expression: {expression}");
        }
    }

    [Test]
    public void rejects_invalid_expressions() {
        var invalidExpressions = new[] {
            "stream =",           // Missing value
            "= 'orders'",         // Missing property
            "stream ~ '[invalid", // Invalid regex
            "stream AND",         // Missing right operand
            "stream $ 'test'"     // Invalid operator
        };

        foreach (var expression in invalidExpressions) {
            var result = FilterExpression.Create(expression);
            result.IsSuccess.ShouldBeFalse($"Should have rejected invalid expression: {expression}");
        }
    }

    [Test]
    public void handles_case_insensitive_keywords() {
        var expressions = new[] {
            "stream = 'orders' and schema.name = 'OrderCreated'",
            "stream = 'orders' AND schema.name = 'OrderCreated'",
            "metadata.userId is null",
            "metadata.userId IS NULL",
            "stream in ('orders', 'payments')",
            "stream IN ('orders', 'payments')"
        };

        foreach (var expression in expressions) {
            var result = FilterExpression.Create(expression);
            result.IsSuccess.ShouldBeTrue($"Failed to parse case variation: {expression}");
        }
    }
}
