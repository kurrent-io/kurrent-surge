using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class ExpressionFilterParserTests {
    readonly ExpressionFilterParser _parser = new();

    #region Comparison Operators

    [Test]
    public async Task parses_equality_expression() {
        var result = _parser.Parse("stream = 'orders'");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsNotNull();
    }

    [Test]
    public async Task parses_not_equals_with_exclamation() {
        var result = _parser.Parse("stream != 'test'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_not_equals_with_angle_brackets() {
        var result = _parser.Parse("stream <> 'test'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_greater_than() {
        var result = _parser.Parse("position > 100");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_less_than() {
        var result = _parser.Parse("position < 100");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_greater_than_or_equal() {
        var result = _parser.Parse("position >= 100");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_less_than_or_equal() {
        var result = _parser.Parse("position <= 100");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    #endregion

    #region Logical Operators

    [Test]
    public async Task parses_and_expression() {
        var result = _parser.Parse("stream = 'orders' AND schema_name = 'OrderCreated'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_or_expression() {
        var result = _parser.Parse("stream = 'orders' OR stream = 'payments'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_not_expression() {
        var result = _parser.Parse("NOT (stream = 'test')");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_complex_logical_expression_with_parentheses() {
        var result = _parser.Parse("(stream = 'orders' OR stream = 'payments') AND properties.amount > 100");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_nested_logical_expressions() {
        var result = _parser.Parse("(stream = 'a' AND schema_name = 'b') OR (stream = 'c' AND schema_name = 'd')");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    #endregion

    #region Pattern Matching

    [Test]
    public async Task parses_like_expression() {
        var result = _parser.Parse("stream LIKE 'order%'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_not_like_expression() {
        var result = _parser.Parse("stream NOT LIKE '%test%'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_ilike_expression() {
        var result = _parser.Parse("schema_name ILIKE '%event%'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_not_ilike_expression() {
        var result = _parser.Parse("stream NOT ILIKE '%TEST%'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_like_with_underscore_wildcard() {
        var result = _parser.Parse("stream LIKE 'order_'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    #endregion

    #region Null Checking

    [Test]
    public async Task parses_is_null() {
        var result = _parser.Parse("properties.correlationId IS NULL");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_is_not_null() {
        var result = _parser.Parse("properties.userId IS NOT NULL");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    #endregion

    #region Set Membership

    [Test]
    public async Task parses_in_expression_with_strings() {
        var result = _parser.Parse("stream IN ('orders', 'payments', 'refunds')");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_in_expression_with_numbers() {
        var result = _parser.Parse("position IN (100, 200, 300)");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_not_in_expression() {
        var result = _parser.Parse("schema_name NOT IN ('SystemEvent', 'HeartbeatEvent')");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_in_with_single_value() {
        var result = _parser.Parse("stream IN ('orders')");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    #endregion

    #region Range Operators

    [Test]
    public async Task parses_between_with_numbers() {
        var result = _parser.Parse("position BETWEEN 100 AND 200");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_between_with_dates() {
        var result = _parser.Parse("timestamp BETWEEN '2024-01-01' AND '2024-12-31'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_not_between() {
        var result = _parser.Parse("position NOT BETWEEN 100 AND 200");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    #endregion

    #region Property Access

    [Test]
    public async Task parses_properties_bracket_notation() {
        var result = _parser.Parse("properties['userId'] = 'user-123'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_properties_dot_notation() {
        var result = _parser.Parse("properties.userId = 'user-123'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_props_alias_bracket_notation() {
        var result = _parser.Parse("props['correlationId'] IS NOT NULL");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_props_alias_dot_notation() {
        var result = _parser.Parse("props.amount > 100");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_properties_with_special_characters_in_key() {
        var result = _parser.Parse("properties['order.total'] > 50");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    #endregion

    #region Data Types

    [Test]
    public async Task parses_string_literals() {
        var result = _parser.Parse("stream = 'orders'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_string_with_escaped_quote() {
        var result = _parser.Parse("properties.name = 'O\\'Reilly'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_integer_literals() {
        var result = _parser.Parse("position = 42");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_negative_integer_literals() {
        var result = _parser.Parse("properties.offset = -10");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_decimal_literals() {
        var result = _parser.Parse("properties.rate = 99.99");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_datetime_iso8601() {
        var result = _parser.Parse("timestamp = '2024-01-15T10:30:00Z'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_datetime_with_timezone() {
        var result = _parser.Parse("timestamp >= '2024-01-01T00:00:00+02:00'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_boolean_true() {
        var result = _parser.Parse("properties.active = true");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_boolean_false() {
        var result = _parser.Parse("properties.deleted = false");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    #endregion

    #region Case Insensitivity

    [Test]
    public async Task parses_lowercase_and_keyword() {
        var result = _parser.Parse("stream = 'orders' and schema_name = 'OrderCreated'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_lowercase_or_keyword() {
        var result = _parser.Parse("stream = 'orders' or stream = 'payments'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_lowercase_not_keyword() {
        var result = _parser.Parse("not (stream = 'test')");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_lowercase_is_null() {
        var result = _parser.Parse("properties.key is null");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_lowercase_in() {
        var result = _parser.Parse("stream in ('orders', 'payments')");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_lowercase_between() {
        var result = _parser.Parse("position between 100 and 200");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_mixed_case_keywords() {
        var result = _parser.Parse("stream = 'orders' And schema_name Like '%Event'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    #endregion

    #region Operator Precedence

    [Test]
    public async Task respects_and_over_or_precedence() {
        var result = _parser.Parse("stream = 'a' OR stream = 'b' AND schema_name = 'c'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task respects_parentheses_over_precedence() {
        var result = _parser.Parse("(stream = 'a' OR stream = 'b') AND schema_name = 'c'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    #endregion

    #region Complex Expressions

    [Test]
    public async Task parses_complex_business_logic_expression() {
        var expression = @"
            stream = 'orders' AND
            properties.amount > 100 AND
            properties.region IN ('us-east', 'us-west') AND
            properties.customerType != 'internal' AND
            timestamp >= '2024-01-01'
        ";

        var result = _parser.Parse(expression);

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_multiple_or_conditions() {
        var result = _parser.Parse("schema_name = 'A' OR schema_name = 'B' OR schema_name = 'C'");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task parses_deeply_nested_expression() {
        var result = _parser.Parse("((stream = 'a' AND schema_name = 'b') OR (stream = 'c' AND schema_name = 'd')) AND position > 100");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    #endregion

    #region Error Cases

    [Test]
    public async Task returns_error_for_empty_input() {
        var result = _parser.Parse("");

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task returns_error_for_whitespace_only_input() {
        var result = _parser.Parse("   ");

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task returns_error_for_missing_value() {
        var result = _parser.Parse("stream =");

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.ErrorMessage).Contains("syntax error");
    }

    [Test]
    public async Task returns_error_for_missing_property() {
        var result = _parser.Parse("= 'orders'");

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.ErrorMessage).Contains("syntax error");
    }

    [Test]
    public async Task returns_error_for_unclosed_string() {
        var result = _parser.Parse("stream = 'orders");

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task returns_error_for_unclosed_parenthesis() {
        var result = _parser.Parse("(stream = 'orders'");

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task returns_error_for_missing_and_operand() {
        var result = _parser.Parse("stream = 'orders' AND");

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task returns_error_for_missing_or_operand() {
        var result = _parser.Parse("stream = 'orders' OR");

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task returns_error_for_invalid_operator() {
        var result = _parser.Parse("stream === 'orders'");

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task returns_error_for_empty_in_list() {
        var result = _parser.Parse("stream IN ()");

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task returns_error_for_missing_between_and() {
        var result = _parser.Parse("position BETWEEN 100 200");

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task returns_error_with_near_token_information() {
        var result = _parser.Parse("stream = 'orders' TOTALLY 'invalid'");

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.ErrorMessage).Contains("TOTALLY");
    }

    #endregion

    #region Span Input

    [Test]
    public async Task accepts_readonly_span_input() {
        ReadOnlySpan<char> input  = "stream = 'orders'";
        var                result = _parser.Parse(input);

        await Assert.That(result.IsSuccess).IsTrue();
    }

    #endregion
}
