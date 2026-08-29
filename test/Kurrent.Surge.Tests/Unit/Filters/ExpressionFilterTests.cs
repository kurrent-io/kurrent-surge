using System.Text.Json.Nodes;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.RecordFilters;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Naming;

namespace Kurrent.Surge.Tests.Filters;

[Category("Filters")]
public class ExpressionFilterTests {
    readonly RecordFaker _faker = new();

    static Filter<Record> Parse(string expression) => RecordFilterSchema.Default.Parse(expression);

    #region Comparison Operators

    [Test]
    public async ValueTask evaluates_stream_equality() {
        var filter            = Parse("stream = 'orders'");
        var matchingRecord    = _faker.With("orders");
        var nonMatchingRecord = _faker.With("payments");

        await Assert.That(filter.IsMatch(matchingRecord)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingRecord)).IsFalse();
    }

    [Test]
    public async ValueTask rejects_stream_equality_differing_only_by_case() {
        // Arrange
        var filter = Parse("stream = 'ORDERS'");
        var record = _faker.With("orders");

        // Act
        var matched = filter.IsMatch(record);

        // Assert
        await Assert.That(matched).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_not_equals() {
        var filter            = Parse("stream != 'test'");
        var matchingRecord    = _faker.With("orders");
        var nonMatchingRecord = _faker.With("test");

        await Assert.That(filter.IsMatch(matchingRecord)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingRecord)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_greater_than() {
        var filter            = Parse("position > 100");
        var matchingRecord    = _faker.With(position: 150);
        var nonMatchingRecord = _faker.With(position: 50);

        await Assert.That(filter.IsMatch(matchingRecord)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingRecord)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_less_than() {
        var filter            = Parse("position < 100");
        var matchingRecord    = _faker.With(position: 50);
        var nonMatchingRecord = _faker.With(position: 150);

        await Assert.That(filter.IsMatch(matchingRecord)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingRecord)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_greater_than_or_equal() {
        var filter          = Parse("position >= 100");
        var matchingEqual   = _faker.With(position: 100);
        var matchingGreater = _faker.With(position: 150);
        var nonMatching     = _faker.With(position: 50);

        await Assert.That(filter.IsMatch(matchingEqual)).IsTrue();
        await Assert.That(filter.IsMatch(matchingGreater)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_less_than_or_equal() {
        var filter        = Parse("position <= 100");
        var matchingEqual = _faker.With(position: 100);
        var matchingLess  = _faker.With(position: 50);
        var nonMatching   = _faker.With(position: 150);

        await Assert.That(filter.IsMatch(matchingEqual)).IsTrue();
        await Assert.That(filter.IsMatch(matchingLess)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_timestamp_comparison() {
        var filter = Parse("timestamp >= 1704067200000"); // 2024-01-01T00:00:00Z in unix millis
        var matchingRecord = _faker.With(
            timestamp: new DateTime(
                2024, 6, 15,
                0, 0, 0,
                DateTimeKind.Utc
            )
        );

        var nonMatchingRecord = _faker.With(
            timestamp: new DateTime(
                2023, 6, 15,
                0, 0, 0,
                DateTimeKind.Utc
            )
        );

        await Assert.That(filter.IsMatch(matchingRecord)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingRecord)).IsFalse();
    }

    #endregion

    #region Logical Operators

    [Test]
    public async ValueTask evaluates_and_expression() {
        var filter         = Parse("stream = 'orders' AND schema_name = 'OrderCreated'");
        var matchingRecord = _faker.With("orders", "OrderCreated");
        var wrongStream    = _faker.With("payments", "OrderCreated");
        var wrongSchema    = _faker.With("orders", "OrderUpdated");

        await Assert.That(filter.IsMatch(matchingRecord)).IsTrue();
        await Assert.That(filter.IsMatch(wrongStream)).IsFalse();
        await Assert.That(filter.IsMatch(wrongSchema)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_or_expression() {
        var filter         = Parse("stream = 'orders' OR stream = 'payments'");
        var ordersRecord   = _faker.With("orders");
        var paymentsRecord = _faker.With("payments");
        var usersRecord    = _faker.With("users");

        await Assert.That(filter.IsMatch(ordersRecord)).IsTrue();
        await Assert.That(filter.IsMatch(paymentsRecord)).IsTrue();
        await Assert.That(filter.IsMatch(usersRecord)).IsFalse();
        
        var properFilter = RecordFilter.ByExpression("stream = 'orders' OR stream = 'payments'");
        await Assert.That(properFilter.As<ExpressionFilter>().ServerFilter).IsTypeOf<StreamRegexFilter>();
    }

    [Test]
    public async ValueTask evaluates_not_expression() {
        var filter            = Parse("NOT (stream = 'test')");
        var matchingRecord    = _faker.With("orders");
        var nonMatchingRecord = _faker.With("test");

        await Assert.That(filter.IsMatch(matchingRecord)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingRecord)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_complex_logical_expression() {
        var filter           = Parse("(stream = 'orders' OR stream = 'payments') AND schema_name = 'Created'");
        var matchingOrders   = _faker.With("orders", "Created");
        var matchingPayments = _faker.With("payments", "Created");
        var wrongSchema      = _faker.With("orders", "Updated");
        var wrongStream      = _faker.With("users", "Created");

        await Assert.That(filter.IsMatch(matchingOrders)).IsTrue();
        await Assert.That(filter.IsMatch(matchingPayments)).IsTrue();
        await Assert.That(filter.IsMatch(wrongSchema)).IsFalse();
        await Assert.That(filter.IsMatch(wrongStream)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_and_with_short_circuit() {
        var filter = Parse("stream = 'never' AND properties['missing'] > 100");
        var record = _faker.With("orders");

        await Assert.That(filter.IsMatch(record)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_or_with_short_circuit() {
        var filter = Parse("stream = 'orders' OR properties['missing'] > 100");
        var record = _faker.With("orders");

        await Assert.That(filter.IsMatch(record)).IsTrue();
    }

    #endregion

    #region Pattern Matching

    [Test]
    public async ValueTask evaluates_like_with_percent_wildcard() {
        var filter      = Parse("stream LIKE 'order%'");
        var matching1   = _faker.With("orders");
        var matching2   = _faker.With("order");
        var matching3   = _faker.With("orders-123");
        var nonMatching = _faker.With("payments");

        await Assert.That(filter.IsMatch(matching1)).IsTrue();
        await Assert.That(filter.IsMatch(matching2)).IsTrue();
        await Assert.That(filter.IsMatch(matching3)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_like_with_underscore_wildcard() {
        var filter       = Parse("stream LIKE 'order_'");
        var matching     = _faker.With("orders");
        var nonMatching1 = _faker.With("order");
        var nonMatching2 = _faker.With("order-123");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching1)).IsFalse();
        await Assert.That(filter.IsMatch(nonMatching2)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_like_with_exact_match() {
        var filter       = Parse("stream LIKE 'orders-99'");
        var matching     = _faker.With("orders-99");
        var nonMatching1 = _faker.With("orders-");
        var nonMatching2 = _faker.With("orders-123");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching1)).IsFalse();
        await Assert.That(filter.IsMatch(nonMatching2)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_like_case_sensitive() {
        var filter      = Parse("stream LIKE 'Order%'");
        var matching    = _faker.With("Orders");
        var nonMatching = _faker.With("orders");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_ilike_case_insensitive() {
        var filter    = Parse("stream ILIKE 'order%'");
        var matching1 = _faker.With("ORDERS-123");
        var matching2 = _faker.With("Orders-456");
        var matching3 = _faker.With("orders-789");

        await Assert.That(filter.IsMatch(matching1)).IsTrue();
        await Assert.That(filter.IsMatch(matching2)).IsTrue();
        await Assert.That(filter.IsMatch(matching3)).IsTrue();
    }

    [Test]
    public async ValueTask evaluates_not_like() {
        var filter      = Parse("stream NOT LIKE '%test%'");
        var matching    = _faker.With("orders");
        var nonMatching = _faker.With("test-stream");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_not_ilike() {
        var filter      = Parse("stream NOT ILIKE '%TEST%'");
        var matching    = _faker.With("orders");
        var nonMatching = _faker.With("my-test-stream");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Null Checking

    [Test]
    public async ValueTask evaluates_is_null_for_missing_property() {
        var filter            = Parse("properties['correlationId'] IS NULL");
        var recordWithoutProp = _faker.With();
        var recordWithProp    = _faker.With(metadata: new Metadata().With("correlationId", "abc"));

        await Assert.That(filter.IsMatch(recordWithoutProp)).IsTrue();
        await Assert.That(filter.IsMatch(recordWithProp)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_is_not_null() {
        var filter            = Parse("properties['userId'] IS NOT NULL");
        var recordWithProp    = _faker.With(metadata: new Metadata().With("userId", "user-123"));
        var recordWithoutProp = _faker.With();

        await Assert.That(filter.IsMatch(recordWithProp)).IsTrue();
        await Assert.That(filter.IsMatch(recordWithoutProp)).IsFalse();
    }

    #endregion

    #region Set Membership

    [Test]
    public async ValueTask evaluates_in_expression() {
        var filter      = Parse("stream IN ('orders', 'payments', 'refunds')");
        var matching1   = _faker.With("orders");
        var matching2   = _faker.With("payments");
        var matching3   = _faker.With("refunds");
        var nonMatching = _faker.With("users");

        await Assert.That(filter.IsMatch(matching1)).IsTrue();
        await Assert.That(filter.IsMatch(matching2)).IsTrue();
        await Assert.That(filter.IsMatch(matching3)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_not_in_expression() {
        var filter      = Parse("schema_name NOT IN ('SystemEvent', 'HeartbeatEvent')");
        var matching    = _faker.With(schemaName: "OrderCreated");
        var nonMatching = _faker.With(schemaName: "SystemEvent");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_in_with_numbers() {
        var filter      = Parse("position IN (100, 200, 300)");
        var matching    = _faker.With(position: 200);
        var nonMatching = _faker.With(position: 150);

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Range Operators

    [Test]
    public async ValueTask evaluates_between_inclusive() {
        var filter           = Parse("position BETWEEN 100 AND 200");
        var matchingLower    = _faker.With(position: 100);
        var matchingMiddle   = _faker.With(position: 150);
        var matchingUpper    = _faker.With(position: 200);
        var nonMatchingBelow = _faker.With(position: 50);
        var nonMatchingAbove = _faker.With(position: 250);

        await Assert.That(filter.IsMatch(matchingLower)).IsTrue();
        await Assert.That(filter.IsMatch(matchingMiddle)).IsTrue();
        await Assert.That(filter.IsMatch(matchingUpper)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingBelow)).IsFalse();
        await Assert.That(filter.IsMatch(nonMatchingAbove)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_between_with_dates() {
        var filter = Parse("timestamp BETWEEN 1704067200000 AND 1735603200000"); // 2024-01-01 to 2024-12-31 in unix millis
        var matching = _faker.With(
            timestamp: new DateTime(
                2024, 6, 15,
                0, 0, 0,
                DateTimeKind.Utc
            )
        );

        var nonMatchingBefore = _faker.With(
            timestamp: new DateTime(
                2023, 12, 31,
                0, 0, 0,
                DateTimeKind.Utc
            )
        );

        var nonMatchingAfter = _faker.With(
            timestamp: new DateTime(
                2025, 1, 1,
                0, 0, 0,
                DateTimeKind.Utc
            )
        );

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingBefore)).IsFalse();
        await Assert.That(filter.IsMatch(nonMatchingAfter)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_not_between() {
        var filter      = Parse("position NOT BETWEEN 100 AND 200");
        var matching1   = _faker.With(position: 50);
        var matching2   = _faker.With(position: 250);
        var nonMatching = _faker.With(position: 150);

        await Assert.That(filter.IsMatch(matching1)).IsTrue();
        await Assert.That(filter.IsMatch(matching2)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Property Access

    [Test]
    public async ValueTask evaluates_properties_bracket_notation() {
        var filter      = Parse("properties['userId'] = 'user-123'");
        var matching    = _faker.With(metadata: new Metadata().With("userId", "user-123"));
        var nonMatching = _faker.With(metadata: new Metadata().With("userId", "user-456"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_properties_dot_notation() {
        var filter      = Parse("properties['amount'] > 100");
        var matching    = _faker.With(metadata: new Metadata().With("amount", 150));
        var nonMatching = _faker.With(metadata: new Metadata().With("amount", 50));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_props_alias() {
        var filter      = Parse("props['correlationId'] IS NOT NULL");
        var matching    = _faker.With(metadata: new Metadata().With("correlationId", "abc-123"));
        var nonMatching = _faker.With();

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_missing_property_as_null() {
        var filter = Parse("properties['missing'] = 'value'");
        var record = _faker.With();

        await Assert.That(filter.IsMatch(record)).IsFalse();
    }

    #endregion

    #region Type Coercion

    [Test]
    public async ValueTask coerces_string_to_number_for_comparison() {
        var filter   = Parse("properties['amount'] > 100");
        var matching = _faker.With(metadata: new Metadata().With("amount", "150"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
    }

    [Test]
    public async ValueTask coerces_number_to_string_for_comparison() {
        var filter   = Parse("properties['code'] = '42'");
        var matching = _faker.With(metadata: new Metadata().With("code", 42));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
    }

    [Test]
    public async ValueTask handles_boolean_property_values() {
        var filter      = Parse("properties['active'] = true");
        var matching    = _faker.With(metadata: new Metadata().With("active", true));
        var nonMatching = _faker.With(metadata: new Metadata().With("active", false));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Complex Expressions

    [Test]
    public async ValueTask evaluates_complex_business_logic() {
        var filter = Parse(
            """
            stream = 'orders' AND
            properties['amount'] > 100 AND
            properties['region'] IN ('us-east', 'us-west') AND
            properties['customerType'] != 'internal'
            """
        );

        var matching = _faker.With(
            "orders",
            metadata: new Metadata()
                .With("amount", 150)
                .With("region", "us-east")
                .With("customerType", "external")
        );

        var wrongAmount = _faker.With(
            "orders",
            metadata: new Metadata()
                .With("amount", 50)
                .With("region", "us-east")
                .With("customerType", "external")
        );

        var wrongRegion = _faker.With(
            "orders",
            metadata: new Metadata()
                .With("amount", 150)
                .With("region", "eu-west")
                .With("customerType", "external")
        );

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(wrongAmount)).IsFalse();
        await Assert.That(filter.IsMatch(wrongRegion)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_nested_or_with_and() {
        var filter = Parse("(schema_name = 'OrderCreated' OR schema_name = 'OrderUpdated') AND stream = 'orders'");

        var matchingCreated = _faker.With("orders", "OrderCreated");
        var matchingUpdated = _faker.With("orders", "OrderUpdated");
        var wrongStream     = _faker.With("payments", "OrderCreated");
        var wrongSchema     = _faker.With("orders", "OrderDeleted");

        await Assert.That(filter.IsMatch(matchingCreated)).IsTrue();
        await Assert.That(filter.IsMatch(matchingUpdated)).IsTrue();
        await Assert.That(filter.IsMatch(wrongStream)).IsFalse();
        await Assert.That(filter.IsMatch(wrongSchema)).IsFalse();
    }

    #endregion

    #region Three-Valued Logic

    [Test]
    public async ValueTask does_not_match_when_a_negated_predicate_reads_a_missing_property() {
        // Arrange — the property resolves to nothing, so the predicate is UNKNOWN, and UNKNOWN
        // survives the NOT rather than flipping to true the way false would
        var filter = Parse("NOT (properties['missing'] = 'anything')");
        var record = _faker.With(metadata: new Metadata().With("region", "us-east"));

        // Act
        var matched = filter.IsMatch(record);

        // Assert
        await Assert.That(matched).IsFalse();
    }

    [Test]
    public async ValueTask does_not_match_when_a_negated_compound_contains_a_missing_property() {
        // Arrange
        var filter = Parse("NOT (properties['missing'] = 'anything' AND stream = 'orders-99')");
        var record = _faker.With("orders-99", metadata: new Metadata().With("region", "us-east"));

        // Act
        var matched = filter.IsMatch(record);

        // Assert
        await Assert.That(matched).IsFalse();
    }

    [Test]
    public async ValueTask does_not_match_when_compared_against_a_null_literal() {
        // Arrange — `x = NULL` is UNKNOWN in SQL, which is why IS NULL exists
        var equals    = Parse("stream = NULL");
        var notEquals = Parse("stream != NULL");
        var negated   = Parse("NOT (stream = NULL)");
        var record    = _faker.With("orders-99");

        // Act & Assert
        await Assert.That(equals.IsMatch(record)).IsFalse();
        await Assert.That(notEquals.IsMatch(record)).IsFalse();
        await Assert.That(negated.IsMatch(record)).IsFalse();
    }

    [Test]
    public async ValueTask still_matches_when_a_negated_predicate_reads_a_present_property() {
        // Arrange — the resolvable case must keep flipping, or the fix would break plain negation
        var filter      = Parse("NOT (properties['region'] = 'eu-west')");
        var matching    = _faker.With(metadata: new Metadata().With("region", "us-east"));
        var nonMatching = _faker.With(metadata: new Metadata().With("region", "eu-west"));

        // Act & Assert
        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Case Sensitivity

    [Test]
    public async ValueTask rejects_in_set_value_differing_only_by_case() {
        // Arrange
        var filter = Parse("stream IN ('ORDERS', 'PAYMENTS')");
        var record = _faker.With("orders");

        // Act
        var matched = filter.IsMatch(record);

        // Assert
        await Assert.That(matched).IsFalse();
    }

    [Test]
    public async ValueTask rejects_category_equality_differing_only_by_case() {
        // Arrange
        var filter = Parse("category = 'ORDERS'");
        var record = _faker.With("orders-99");

        // Act
        var matched = filter.IsMatch(record);

        // Assert
        await Assert.That(matched).IsFalse();
    }

    [Test]
    public async ValueTask rejects_schema_name_equality_differing_only_by_case() {
        // Arrange
        var filter = Parse("schema_name = 'ORDERPLACED'");
        var record = _faker.With(schemaName: "OrderPlaced");

        // Act
        var matched = filter.IsMatch(record);

        // Assert
        await Assert.That(matched).IsFalse();
    }

    [Test]
    public async ValueTask rejects_metadata_value_differing_only_by_case() {
        // Arrange
        var filter = Parse("properties['region'] = 'US-EAST'");
        var record = _faker.With(metadata: new Metadata().With("region", "us-east"));

        // Act
        var matched = filter.IsMatch(record);

        // Assert
        await Assert.That(matched).IsFalse();
    }

    [Test]
    public async ValueTask matches_ilike_pattern_differing_only_by_case() {
        // Arrange — ILIKE stays case-insensitive by definition; equality does not
        var filter = Parse("stream ILIKE 'ORDERS%'");
        var record = _faker.With("orders-99");

        // Act
        var matched = filter.IsMatch(record);

        // Assert
        await Assert.That(matched).IsTrue();
    }

    #endregion

    #region Column Aliases

    [Test]
    public async ValueTask evaluates_schema_name_column() {
        var filter      = Parse("schema_name = 'OrderPlaced'");
        var matching    = _faker.With(schemaName: "OrderPlaced");
        var nonMatching = _faker.With(schemaName: "OrderShipped");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_revision_alias() {
        var filter      = Parse("revision > 5");
        var matching    = _faker.With(streamRevision: 10);
        var nonMatching = _faker.With(streamRevision: 3);

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_log_position_alias() {
        var filter      = Parse("log_position > 1000");
        var matching    = _faker.With(position: 1500);
        var nonMatching = _faker.With(position: 500);

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_created_at_alias() {
        var filter = Parse("created_at >= 1767225600000"); // 2026-01-01 in unix millis
        var matching = _faker.With(
            timestamp: new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)
        );

        var nonMatching = _faker.With(
            timestamp: new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc)
        );

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_ts_alias() {
        var filter = Parse("ts >= 1767225600000"); // 2026-01-01 in unix millis
        var matching = _faker.With(
            timestamp: new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)
        );

        var nonMatching = _faker.With(
            timestamp: new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc)
        );

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_metadata_alias() {
        var filter      = Parse("metadata['region'] = 'us-east'");
        var matching    = _faker.With(metadata: new Metadata().With("region", "us-east"));
        var nonMatching = _faker.With(metadata: new Metadata().With("region", "eu-west"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_stream_column() {
        var filter      = Parse("stream = 'orders-99'");
        var matching    = _faker.With("orders-99");
        var nonMatching = _faker.With("payments-1");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Property Access via Dot-Path Fallback

    [Test]
    public async ValueTask evaluates_metadata_dot_path_key_access() {
        var filter      = Parse("properties['region'] = 'us-east'");
        var matching    = _faker.With(metadata: new Metadata().With("region", "us-east"));
        var nonMatching = _faker.With(metadata: new Metadata().With("region", "eu-west"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Category Derivation

    [Test]
    public async ValueTask evaluates_category_from_stream_with_delimiter() {
        var filter      = Parse("category = 'order'");
        var matching    = _faker.With("order-abc123");
        var nonMatching = _faker.With("payment-2026-001");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_category_from_stream_with_multiple_delimiters() {
        var filter   = Parse("category = 'payment'");
        var matching = _faker.With("payment-2026-001");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
    }

    [Test]
    public async ValueTask evaluates_category_from_stream_without_delimiter() {
        var filter      = Parse("category = 'nodelimiter'");
        var matching    = _faker.With("nodelimiter");
        var nonMatching = _faker.With("other");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Record Prefix Rejection

    public sealed class RecPrefixedColumns : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("rec.stream = 'orders-99'",              "stream");
            yield return new("rec.category = 'orders'",               "category");
            yield return new("rec.schema_name = 'OrderPlaced'",       "schema_name");
            yield return new("rec.properties['region'] = 'us-east'",  "metadata key");
        }
    }

    [Test]
    [RecPrefixedColumns]
    public async ValueTask rejects_rec_prefixed_column(string expression) {
        // Act & Assert
        await Assert.That(() => Parse(expression)).Throws<FilterFormatException>();
    }

    #endregion

    #region Scalar Field Access

    public sealed class ScalarFieldColumns : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("schema.name = 'OrderPlaced'",   "dotted path onto an alias");
            yield return new("stream.category = 'orders'",    "dotted path onto a canonical column");
            yield return new("stream['category'] = 'orders'", "bracket key");
            yield return new("stream->>'category' = 'orders'", "arrow key");
        }
    }

    [Test]
    [ScalarFieldColumns]
    public async ValueTask rejects_a_field_read_from_a_scalar_column(string expression) {
        // Act & Assert
        await Assert.That(() => Parse(expression)).Throws<FilterFormatException>();
    }

    #endregion

    #region Data Payload Access

    [Test]
    public async ValueTask evaluates_data_scalar_field() {
        // Arrange
        var filter      = Parse("data.amount > 100");
        var matching    = RecordWithPayload("""{"amount":150}""");
        var nonMatching = RecordWithPayload("""{"amount":50}""");

        // Act & Assert
        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_data_nested_field() {
        // Arrange
        var filter      = Parse("data.address.city = 'lisbon'");
        var matching    = RecordWithPayload("""{"address":{"city":"lisbon"}}""");
        var nonMatching = RecordWithPayload("""{"address":{"city":"porto"}}""");

        // Act & Assert
        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_data_arrow_key_access() {
        // Arrange
        var filter      = Parse("data->>'status' = 'shipped'");
        var matching    = RecordWithPayload("""{"status":"shipped"}""");
        var nonMatching = RecordWithPayload("""{"status":"pending"}""");

        // Act & Assert
        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask does_not_match_data_field_when_record_is_not_decoded() {
        // Arrange — the default decoder projects no JSON, so every data predicate is unresolvable
        var filter = Parse("data.amount > 100");
        var record = _faker.With("orders-1");

        // Act
        var matched = filter.IsMatch(record);

        // Assert
        await Assert.That(matched).IsFalse();
    }

    [Test]
    public async ValueTask reports_data_as_present_when_the_record_carries_a_payload() {
        // Arrange
        var isNull    = Parse("data IS NULL");
        var isNotNull = Parse("data IS NOT NULL");
        var record    = RecordWithPayload("""{"amount":150}""");

        // Act & Assert
        await Assert.That(isNull.IsMatch(record)).IsFalse();
        await Assert.That(isNotNull.IsMatch(record)).IsTrue();
    }

    [Test]
    public async ValueTask reports_data_as_absent_when_the_record_is_not_decoded() {
        // Arrange
        var isNull    = Parse("data IS NULL");
        var isNotNull = Parse("data IS NOT NULL");
        var record    = _faker.With("orders-1");

        // Act & Assert
        await Assert.That(isNull.IsMatch(record)).IsTrue();
        await Assert.That(isNotNull.IsMatch(record)).IsFalse();
    }

    [Test]
    public async ValueTask reports_metadata_as_present_even_when_the_bag_is_empty() {
        // Arrange — the bag always exists, so the column is '{}', never SQL NULL
        var isNull    = Parse("metadata IS NULL");
        var isNotNull = Parse("properties IS NOT NULL");
        var record    = _faker.With("orders-1", metadata: new Metadata());

        // Act & Assert
        await Assert.That(isNull.IsMatch(record)).IsFalse();
        await Assert.That(isNotNull.IsMatch(record)).IsTrue();
    }

    static Record RecordWithPayload(string json) =>
        new(new StubJsonDecoder(json)) {
            Id        = RecordId.New(),
            Position  = new RecordPosition {
                LogPosition    = LogPosition.From(1),
                StreamPosition = new StreamPosition(StreamName.From("orders-1"), Revision.From(1))
            },
            Timestamp = DateTime.UtcNow,
            Schema    = new RecordSchemaInfo {
                Schema     = SchemaName.From("OrderPlaced"),
                DataFormat = SchemaDataFormat.Json
            }
        };

    sealed class StubJsonDecoder(string json) : IRecordDecoder {
        public ValueTask<DecodeOutcome> TryDecode(Record record, CancellationToken ct) =>
            ValueTask.FromResult(DecodeOutcome.Decoded);

        public JsonNode? ProjectToJson(Record record) => JsonNode.Parse(json);
    }

    #endregion

    #region . JSON Access Notation .

    [Test]
    public async ValueTask evaluates_nested_dot_notation() {
        var filter      = Parse("metadata.region = 'us-east'");
        var matching    = _faker.With(metadata: new Metadata().With("region", "us-east"));
        var nonMatching = _faker.With(metadata: new Metadata().With("region", "eu-west"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_arrow_text_string_comparison() {
        var filter      = Parse("properties->>'region' = 'us-east'");
        var matching    = _faker.With(metadata: new Metadata().With("region", "us-east"));
        var nonMatching = _faker.With(metadata: new Metadata().With("region", "eu-west"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_arrow_is_not_null() {
        var filter            = Parse("properties->>'userId' IS NOT NULL");
        var recordWithProp    = _faker.With(metadata: new Metadata().With("userId", "user-123"));
        var recordWithoutProp = _faker.With();

        await Assert.That(filter.IsMatch(recordWithProp)).IsTrue();
        await Assert.That(filter.IsMatch(recordWithoutProp)).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_arrow_numeric_comparison() {
        var filter      = Parse("properties->>'amount' > 100");
        var matching    = _faker.With(metadata: new Metadata().With("amount", "150"));
        var nonMatching = _faker.With(metadata: new Metadata().With("amount", "50"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Edge Cases

    [Test]
    public async ValueTask handles_empty_string_comparison() {
        var filter      = Parse("properties['tag'] = ''");
        var matching    = _faker.With(metadata: new Metadata().With("tag", ""));
        var nonMatching = _faker.With(metadata: new Metadata().With("tag", "value"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask handles_zero_in_numeric_comparison() {
        var filter      = Parse("position = 0");
        var matching    = _faker.With(position: 0);
        var nonMatching = _faker.With(position: 1);

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async ValueTask handles_negative_numbers() {
        var filter      = Parse("properties['offset'] < 0");
        var matching    = _faker.With(metadata: new Metadata().With("offset", -10));
        var nonMatching = _faker.With(metadata: new Metadata().With("offset", 10));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion
}
