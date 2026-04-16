using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class ExpressionFilterParserTests {
    readonly RecordFaker _faker = new();

    #region Comparison Operators

    [Test]
    public async Task parses_equality_expression() {
        var expression = ExpressionFilterParser.Parse("stream = 'orders'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_not_equals_with_exclamation() {
        var expression = ExpressionFilterParser.Parse("stream != 'test'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_not_equals_with_angle_brackets() {
        var expression = ExpressionFilterParser.Parse("stream <> 'test'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_greater_than() {
        var expression = ExpressionFilterParser.Parse("position > 100");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_less_than() {
        var expression = ExpressionFilterParser.Parse("position < 100");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_greater_than_or_equal() {
        var expression = ExpressionFilterParser.Parse("position >= 100");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_less_than_or_equal() {
        var expression = ExpressionFilterParser.Parse("position <= 100");

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region Logical Operators

    [Test]
    public async Task parses_and_expression() {
        var expression = ExpressionFilterParser.Parse("stream = 'orders' AND schema_name = 'OrderCreated'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_or_expression() {
        var expression = ExpressionFilterParser.Parse("stream = 'orders' OR stream = 'payments'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_not_expression() {
        var expression = ExpressionFilterParser.Parse("NOT (stream = 'test')");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_complex_logical_expression_with_parentheses() {
        var expression = ExpressionFilterParser.Parse("(stream = 'orders' OR stream = 'payments') AND properties['amount'] > 100");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_nested_logical_expressions() {
        var expression = ExpressionFilterParser.Parse("(stream = 'a' AND schema_name = 'b') OR (stream = 'c' AND schema_name = 'd')");

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region Pattern Matching

    [Test]
    public async Task parses_like_expression() {
        var expression = ExpressionFilterParser.Parse("stream LIKE 'order%'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_not_like_expression() {
        var expression = ExpressionFilterParser.Parse("stream NOT LIKE '%test%'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_ilike_expression() {
        var expression = ExpressionFilterParser.Parse("schema_name ILIKE '%event%'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_not_ilike_expression() {
        var expression = ExpressionFilterParser.Parse("stream NOT ILIKE '%TEST%'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_like_with_underscore_wildcard() {
        var expression = ExpressionFilterParser.Parse("stream LIKE 'order_'");

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region Null Checking

    [Test]
    public async Task parses_is_null() {
        var expression = ExpressionFilterParser.Parse("properties['correlationId'] IS NULL");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_is_not_null() {
        var expression = ExpressionFilterParser.Parse("properties['userId'] IS NOT NULL");

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region Set Membership

    [Test]
    public async Task parses_in_expression_with_strings() {
        var expression = ExpressionFilterParser.Parse("stream IN ('orders', 'payments', 'refunds')");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_in_expression_with_numbers() {
        var expression = ExpressionFilterParser.Parse("position IN (100, 200, 300)");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_not_in_expression() {
        var expression = ExpressionFilterParser.Parse("schema_name NOT IN ('SystemEvent', 'HeartbeatEvent')");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_in_with_single_value() {
        var expression = ExpressionFilterParser.Parse("stream IN ('orders')");

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region Range Operators

    [Test]
    public async Task parses_between_with_numbers() {
        var expression = ExpressionFilterParser.Parse("position BETWEEN 100 AND 200");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_between_with_dates() {
        var expression = ExpressionFilterParser.Parse("timestamp BETWEEN '2024-01-01' AND '2024-12-31'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_not_between() {
        var expression = ExpressionFilterParser.Parse("position NOT BETWEEN 100 AND 200");

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region Property Access

    [Test]
    public async Task parses_properties_bracket_notation() {
        var expression = ExpressionFilterParser.Parse("properties['userId'] = 'user-123'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_properties_dot_notation() {
        var expression = ExpressionFilterParser.Parse("properties['userId'] = 'user-123'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_props_alias_bracket_notation() {
        var expression = ExpressionFilterParser.Parse("props['correlationId'] IS NOT NULL");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_props_alias_dot_notation() {
        var expression = ExpressionFilterParser.Parse("props['amount'] > 100");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_properties_with_special_characters_in_key() {
        var expression = ExpressionFilterParser.Parse("properties['order.total'] > 50");

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region Data Types

    [Test]
    public async Task parses_string_literals() {
        var expression = ExpressionFilterParser.Parse("stream = 'orders'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_string_with_escaped_quote() {
        var expression = ExpressionFilterParser.Parse("properties['name'] = 'O\\'Reilly'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_integer_literals() {
        var expression = ExpressionFilterParser.Parse("position = 42");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_negative_integer_literals() {
        var expression = ExpressionFilterParser.Parse("properties['offset'] = -10");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_decimal_literals() {
        var expression = ExpressionFilterParser.Parse("properties['rate'] = 99.99");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_datetime_iso8601() {
        var expression = ExpressionFilterParser.Parse("timestamp = '2024-01-15T10:30:00Z'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_datetime_with_timezone() {
        var expression = ExpressionFilterParser.Parse("timestamp >= '2024-01-01T00:00:00+02:00'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_boolean_true() {
        var expression = ExpressionFilterParser.Parse("properties['active'] = true");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_boolean_false() {
        var expression = ExpressionFilterParser.Parse("properties['deleted'] = false");

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region Case Insensitivity

    [Test]
    public async Task parses_lowercase_and_keyword() {
        var expression = ExpressionFilterParser.Parse("stream = 'orders' and schema_name = 'OrderCreated'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_lowercase_or_keyword() {
        var expression = ExpressionFilterParser.Parse("stream = 'orders' or stream = 'payments'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_lowercase_not_keyword() {
        var expression = ExpressionFilterParser.Parse("not (stream = 'test')");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_lowercase_is_null() {
        var expression = ExpressionFilterParser.Parse("properties['key'] is null");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_lowercase_in() {
        var expression = ExpressionFilterParser.Parse("stream in ('orders', 'payments')");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_lowercase_between() {
        var expression = ExpressionFilterParser.Parse("position between 100 and 200");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_mixed_case_keywords() {
        var expression = ExpressionFilterParser.Parse("stream = 'orders' And schema_name Like '%Event'");

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region Operator Precedence

    [Test]
    public async Task respects_and_over_or_precedence() {
        var expression = ExpressionFilterParser.Parse("stream = 'a' OR stream = 'b' AND schema_name = 'c'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task respects_parentheses_over_precedence() {
        var expression = ExpressionFilterParser.Parse("(stream = 'a' OR stream = 'b') AND schema_name = 'c'");

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region Complex Expressions

    [Test]
    public async Task parses_complex_business_logic_expression() {
        var expression = @"
            stream = 'orders' AND
            properties['amount'] > 100 AND
            properties['region'] IN ('us-east', 'us-west') AND
            properties['customerType'] != 'internal' AND
            timestamp >= '2024-01-01'
        ";

        var result = ExpressionFilterParser.Parse(expression);

        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task parses_multiple_or_conditions() {
        var expression = ExpressionFilterParser.Parse("schema_name = 'A' OR schema_name = 'B' OR schema_name = 'C'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_deeply_nested_expression() {
        var expression = ExpressionFilterParser.Parse("((stream = 'a' AND schema_name = 'b') OR (stream = 'c' AND schema_name = 'd')) AND position > 100");

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region Error Cases

    [Test]
    public async Task returns_error_for_empty_input() {
        var success = ExpressionFilterParser.TryParse("", out _, out var error);

        await Assert.That(success).IsFalse();
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task returns_error_for_whitespace_only_input() {
        var success = ExpressionFilterParser.TryParse("   ", out _, out var error);

        await Assert.That(success).IsFalse();
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task returns_error_for_missing_value() {
        var success = ExpressionFilterParser.TryParse("stream =", out _, out var error);

        await Assert.That(success).IsFalse();
        await Assert.That(error!.ErrorMessage).Contains("syntax error");
    }

    [Test]
    public async Task returns_error_for_missing_property() {
        var success = ExpressionFilterParser.TryParse("= 'orders'", out _, out var error);

        await Assert.That(success).IsFalse();
        await Assert.That(error!.ErrorMessage).Contains("syntax error");
    }

    [Test]
    public async Task returns_error_for_unclosed_string() {
        var success = ExpressionFilterParser.TryParse("stream = 'orders", out _, out var error);

        await Assert.That(success).IsFalse();
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task returns_error_for_unclosed_parenthesis() {
        var success = ExpressionFilterParser.TryParse("(stream = 'orders'", out _, out var error);

        await Assert.That(success).IsFalse();
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task returns_error_for_missing_and_operand() {
        var success = ExpressionFilterParser.TryParse("stream = 'orders' AND", out _, out var error);

        await Assert.That(success).IsFalse();
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task returns_error_for_missing_or_operand() {
        var success = ExpressionFilterParser.TryParse("stream = 'orders' OR", out _, out var error);

        await Assert.That(success).IsFalse();
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task returns_error_for_invalid_operator() {
        var success = ExpressionFilterParser.TryParse("stream === 'orders'", out _, out var error);

        await Assert.That(success).IsFalse();
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task returns_error_for_empty_in_list() {
        var success = ExpressionFilterParser.TryParse("stream IN ()", out _, out var error);

        await Assert.That(success).IsFalse();
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task returns_error_for_missing_between_and() {
        var success = ExpressionFilterParser.TryParse("position BETWEEN 100 200", out _, out var error);

        await Assert.That(success).IsFalse();
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task returns_error_with_near_token_information() {
        var success = ExpressionFilterParser.TryParse("stream = 'orders' TOTALLY 'invalid'", out _, out var error);

        await Assert.That(success).IsFalse();
        await Assert.That(error!.ErrorMessage).Contains("TOTALLY");
    }

    #endregion

    #region Span Input

    [Test]
    public async Task accepts_readonly_span_input() {
        ReadOnlySpan<char> input      = "stream = 'orders'";
        var                expression = ExpressionFilterParser.Parse(input);

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region Multi-Segment Dot Paths

    [Test]
    public async Task parses_two_segment_dot_path() {
        var expression = ExpressionFilterParser.Parse("rec.stream = 'orders-99'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_three_segment_dot_path() {
        var expression = ExpressionFilterParser.Parse("rec.schema.name = 'OrderPlaced'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_position_stream_dot_path() {
        var expression = ExpressionFilterParser.Parse("rec.position.stream = 'orders-99'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_position_stream_revision_dot_path() {
        var expression = ExpressionFilterParser.Parse("rec.position.streamRevision > 5");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_position_log_position_dot_path() {
        var expression = ExpressionFilterParser.Parse("rec.position.logPosition > 1000");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_metadata_dot_path_as_property_key() {
        var expression = ExpressionFilterParser.Parse("metadata['region'] = 'us-east'");

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region Column Aliases

    [Test]
    public async Task parses_event_type_alias() {
        var expression = ExpressionFilterParser.Parse("event_type = 'OrderPlaced'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_event_number_alias() {
        var expression = ExpressionFilterParser.Parse("event_number > 5");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_log_position_alias() {
        var expression = ExpressionFilterParser.Parse("log_position > 1000");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_created_at_alias() {
        var expression = ExpressionFilterParser.Parse("created_at >= '2026-01-01'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_ts_alias() {
        var expression = ExpressionFilterParser.Parse("ts >= 1767225600000"); // 2026-01-01 in unix millis

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_metadata_alias() {
        var expression = ExpressionFilterParser.Parse("metadata['region'] = 'us-east'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_stream_name_alias() {
        var expression = ExpressionFilterParser.Parse("stream = 'orders-99'");

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region Field Prefix Stripping

    [Test]
    public async Task parses_field_stream_alias() {
        var expression = ExpressionFilterParser.Parse("field_stream = 'orders-99'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_field_category_alias() {
        var expression = ExpressionFilterParser.Parse("field_category = 'orders'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_field_schema_alias() {
        var expression = ExpressionFilterParser.Parse("field_schema = 'OrderPlaced'");

        await Assert.That(expression).IsNotNull();
    }

    #endregion

    #region . JSON Access Notation .

    [Test]
    public async Task parses_nested_dot_access() {
        var expression = ExpressionFilterParser.Parse("data.address.city = 'NYC'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_deeply_nested_dot_access() {
        var expression = ExpressionFilterParser.Parse("data.user.profile.settings.theme = 'dark'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_arrow_text_property_access() {
        var expression = ExpressionFilterParser.Parse("properties->>'region' = 'us-east'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task single_arrow_is_not_supported() {
        var success = ExpressionFilterParser.TryParse("data->'tags' IS NOT NULL", out _, out _);

        await Assert.That(success).IsFalse();
    }

    [Test]
    public async Task parses_arrow_after_dot_segments() {
        var expression = ExpressionFilterParser.Parse("rec.properties->>'userId' = 'user-123'");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_arrow_in_complex_expression() {
        var expression = ExpressionFilterParser.Parse("stream = 'orders' AND properties->>'amount' > 100");

        await Assert.That(expression).IsNotNull();
    }

    [Test]
    public async Task parses_negative_number_still_works() {
        var expression = ExpressionFilterParser.Parse("position > -10");
        var record     = _faker.With(position: 5);

        await Assert.That(expression).IsNotNull();
        await Assert.That(expression.IsMatch(record)).IsTrue();
    }

    #endregion

    #region LIKE Fast-Path Patterns

    [Test]
    public async Task like_prefix_pattern_matches() {
        var expr   = ExpressionFilterParser.Parse("stream LIKE 'orders-%'");
        var record = _faker.With(stream: "orders-123");
        await Assert.That(expr.IsMatch(record)).IsTrue();
    }

    [Test]
    public async Task like_prefix_pattern_rejects() {
        var expr   = ExpressionFilterParser.Parse("stream LIKE 'orders-%'");
        var record = _faker.With(stream: "users-123");
        await Assert.That(expr.IsMatch(record)).IsFalse();
    }

    [Test]
    public async Task like_suffix_pattern_matches() {
        var expr   = ExpressionFilterParser.Parse("stream LIKE '%-created'");
        var record = _faker.With(stream: "order-created");
        await Assert.That(expr.IsMatch(record)).IsTrue();
    }

    [Test]
    public async Task like_contains_pattern_matches() {
        var expr   = ExpressionFilterParser.Parse("schema LIKE '%User%'");
        var record = _faker.With(schemaName: "CreateUserV1");
        await Assert.That(expr.IsMatch(record)).IsTrue();
    }

    [Test]
    public async Task like_exact_pattern_matches() {
        var expr   = ExpressionFilterParser.Parse("stream LIKE 'orders-123'");
        var record = _faker.With(stream: "orders-123");
        await Assert.That(expr.IsMatch(record)).IsTrue();
    }

    [Test]
    public async Task like_exact_pattern_rejects() {
        var expr   = ExpressionFilterParser.Parse("stream LIKE 'orders-123'");
        var record = _faker.With(stream: "orders-456");
        await Assert.That(expr.IsMatch(record)).IsFalse();
    }

    [Test]
    public async Task like_matchall_pattern() {
        var expr   = ExpressionFilterParser.Parse("stream LIKE '%'");
        var record = _faker.With(stream: "anything-at-all");
        await Assert.That(expr.IsMatch(record)).IsTrue();
    }

    [Test]
    public async Task like_complex_underscore_pattern() {
        var expr = ExpressionFilterParser.Parse("stream LIKE 'order_123'");
        var hit  = _faker.With(stream: "orders123");
        var miss = _faker.With(stream: "orderXX123");
        await Assert.That(expr.IsMatch(hit)).IsTrue();
        await Assert.That(expr.IsMatch(miss)).IsFalse();
    }

    [Test]
    public async Task ilike_prefix_case_insensitive() {
        var expr   = ExpressionFilterParser.Parse("stream ILIKE 'ORDERS-%'");
        var record = _faker.With(stream: "orders-123");
        await Assert.That(expr.IsMatch(record)).IsTrue();
    }

    #endregion
}
