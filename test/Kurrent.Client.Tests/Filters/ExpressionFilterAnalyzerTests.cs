using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class ExpressionFilterAnalyzerTests {
    readonly ExpressionFilterParser _parser = new();

    #region Single Stream Extraction

    [Test]
    public void extracts_target_stream_from_equality() {
        var expr   = ParseExpression("stream = 'orders'");
        var result = ExpressionFilterParser.Analyze(expr);

        result.TargetStream.ShouldNotBeNull();
        result.TargetStream!.Value.Value.ShouldBe("orders");
        result.ServerScope.ShouldBe(FilterScope.StreamName);
        result.ServerRegex.ShouldBe("^orders$");
        result.HasClientFilter.ShouldBeFalse();
    }

    [Test]
    public void no_target_stream_for_like_pattern() {
        var expr   = ParseExpression("stream LIKE 'order%'");
        var result = ExpressionFilterParser.Analyze(expr);

        result.TargetStream.ShouldBeNull();
        result.ServerScope.ShouldBe(FilterScope.StreamName);
        result.ServerRegex.ShouldNotBeNull();
        result.HasClientFilter.ShouldBeFalse();
    }

    [Test]
    public void no_target_stream_for_in_expression() {
        var expr   = ParseExpression("stream IN ('orders', 'payments')");
        var result = ExpressionFilterParser.Analyze(expr);

        result.TargetStream.ShouldBeNull();
        result.ServerScope.ShouldBe(FilterScope.StreamName);
        result.ServerRegex.ShouldBe("^(orders|payments)$");
        result.HasClientFilter.ShouldBeFalse();
    }

    #endregion

    #region Server-Side Stream Filters

    [Test]
    public void converts_stream_equality_to_exact_regex() {
        var expr   = ParseExpression("stream = 'orders'");
        var result = ExpressionFilterParser.Analyze(expr);

        result.ServerRegex.ShouldBe("^orders$");
        result.ServerScope.ShouldBe(FilterScope.StreamName);
    }

    [Test]
    public void converts_stream_like_prefix_to_regex() {
        var expr   = ParseExpression("stream LIKE 'order%'");
        var result = ExpressionFilterParser.Analyze(expr);

        result.ServerRegex.ShouldBe("^order.*$");
        result.ServerScope.ShouldBe(FilterScope.StreamName);
    }

    [Test]
    public void converts_stream_like_suffix_to_regex() {
        var expr   = ParseExpression("stream LIKE '%orders'");
        var result = ExpressionFilterParser.Analyze(expr);

        result.ServerRegex.ShouldBe("^.*orders$");
        result.ServerScope.ShouldBe(FilterScope.StreamName);
    }

    [Test]
    public void converts_stream_like_contains_to_regex() {
        var expr   = ParseExpression("stream LIKE '%order%'");
        var result = ExpressionFilterParser.Analyze(expr);

        result.ServerRegex.ShouldBe("^.*order.*$");
        result.ServerScope.ShouldBe(FilterScope.StreamName);
    }

    [Test]
    public void converts_stream_in_to_alternation_regex() {
        var expr   = ParseExpression("stream IN ('orders', 'payments', 'refunds')");
        var result = ExpressionFilterParser.Analyze(expr);

        result.ServerRegex.ShouldBe("^(orders|payments|refunds)$");
        result.ServerScope.ShouldBe(FilterScope.StreamName);
    }

    #endregion

    #region Server-Side Schema Filters

    [Test]
    public void converts_schema_name_equality_to_exact_regex() {
        var expr   = ParseExpression("schema_name = 'OrderCreated'");
        var result = ExpressionFilterParser.Analyze(expr);

        result.ServerRegex.ShouldBe("^OrderCreated$");
        result.ServerScope.ShouldBe(FilterScope.SchemaName);
    }

    [Test]
    public void converts_schema_alias_to_exact_regex() {
        var expr   = ParseExpression("schema = 'OrderCreated'");
        var result = ExpressionFilterParser.Analyze(expr);

        result.ServerRegex.ShouldBe("^OrderCreated$");
        result.ServerScope.ShouldBe(FilterScope.SchemaName);
    }

    [Test]
    public void converts_schema_like_to_regex() {
        var expr   = ParseExpression("schema_name LIKE 'Order%'");
        var result = ExpressionFilterParser.Analyze(expr);

        result.ServerRegex.ShouldBe("^Order.*$");
        result.ServerScope.ShouldBe(FilterScope.SchemaName);
    }

    [Test]
    public void converts_schema_in_to_alternation_regex() {
        var expr   = ParseExpression("schema_name IN ('OrderCreated', 'OrderUpdated')");
        var result = ExpressionFilterParser.Analyze(expr);

        result.ServerRegex.ShouldBe("^(OrderCreated|OrderUpdated)$");
        result.ServerScope.ShouldBe(FilterScope.SchemaName);
    }

    #endregion

    #region Client-Side Filters

    [Test]
    public void timestamp_filter_goes_to_client() {
        var expr   = ParseExpression("timestamp >= '2024-01-01'");
        var result = ExpressionFilterParser.Analyze(expr);

        result.HasServerFilter.ShouldBeFalse();
        result.HasClientFilter.ShouldBeTrue();
        result.ClientExpression.ShouldNotBeNull();
    }

    [Test]
    public void position_filter_goes_to_client() {
        var expr   = ParseExpression("position > 1000");
        var result = ExpressionFilterParser.Analyze(expr);

        result.HasServerFilter.ShouldBeFalse();
        result.HasClientFilter.ShouldBeTrue();
    }

    [Test]
    public void properties_filter_goes_to_client() {
        var expr   = ParseExpression("properties.amount > 100");
        var result = ExpressionFilterParser.Analyze(expr);

        result.HasServerFilter.ShouldBeFalse();
        result.HasClientFilter.ShouldBeTrue();
    }

    [Test]
    public void revision_filter_goes_to_client() {
        var expr   = ParseExpression("stream_revision > 10");
        var result = ExpressionFilterParser.Analyze(expr);

        result.HasServerFilter.ShouldBeFalse();
        result.HasClientFilter.ShouldBeTrue();
    }

    #endregion

    #region Combined Server and Client Filters

    [Test]
    public void splits_stream_and_timestamp_filter() {
        var expr   = ParseExpression("stream = 'orders' AND timestamp >= '2024-01-01'");
        var result = ExpressionFilterParser.Analyze(expr);

        result.HasServerFilter.ShouldBeTrue();
        result.ServerRegex.ShouldBe("^orders$");
        result.ServerScope.ShouldBe(FilterScope.StreamName);
        result.TargetStream!.Value.Value.ShouldBe("orders");
        result.HasClientFilter.ShouldBeTrue();
    }

    [Test]
    public void splits_stream_and_properties_filter() {
        var expr   = ParseExpression("stream = 'orders' AND properties.amount > 100");
        var result = ExpressionFilterParser.Analyze(expr);

        result.HasServerFilter.ShouldBeTrue();
        result.ServerRegex.ShouldBe("^orders$");
        result.HasClientFilter.ShouldBeTrue();
    }

    [Test]
    public void splits_schema_and_properties_filter() {
        var expr   = ParseExpression("schema_name = 'OrderCreated' AND properties.userId = 'user-123'");
        var result = ExpressionFilterParser.Analyze(expr);

        result.HasServerFilter.ShouldBeTrue();
        result.ServerScope.ShouldBe(FilterScope.SchemaName);
        result.HasClientFilter.ShouldBeTrue();
    }

    [Test]
    public void multiple_client_predicates_combined() {
        var expr   = ParseExpression("stream = 'orders' AND properties.amount > 100 AND timestamp >= '2024-01-01'");
        var result = ExpressionFilterParser.Analyze(expr);

        result.HasServerFilter.ShouldBeTrue();
        result.HasClientFilter.ShouldBeTrue();
        // Client expression should contain both properties.amount and timestamp
    }

    #endregion

    #region Negated Expressions

    [Test]
    public void negated_stream_filter_goes_to_client() {
        var expr   = ParseExpression("stream != 'test'");
        var result = ExpressionFilterParser.Analyze(expr);

        // Negated comparisons cannot be lifted to server
        result.HasServerFilter.ShouldBeFalse();
        result.HasClientFilter.ShouldBeTrue();
    }

    [Test]
    public void negated_like_goes_to_client() {
        var expr   = ParseExpression("stream NOT LIKE 'test%'");
        var result = ExpressionFilterParser.Analyze(expr);

        result.HasServerFilter.ShouldBeFalse();
        result.HasClientFilter.ShouldBeTrue();
    }

    [Test]
    public void negated_in_goes_to_client() {
        var expr   = ParseExpression("stream NOT IN ('test', 'demo')");
        var result = ExpressionFilterParser.Analyze(expr);

        result.HasServerFilter.ShouldBeFalse();
        result.HasClientFilter.ShouldBeTrue();
    }

    #endregion

    #region OR Expressions

    [Test]
    public void or_expression_goes_to_client() {
        var expr   = ParseExpression("stream = 'orders' OR stream = 'payments'");
        var result = ExpressionFilterParser.Analyze(expr);

        // OR expressions cannot be lifted to server as a single AND-flattened predicate
        result.HasClientFilter.ShouldBeTrue();
    }

    #endregion

    #region Mixed Scope Filters

    [Test]
    public void stream_and_schema_filters_uses_first_scope() {
        // When we have both stream and schema_name filters, only one can go to server
        var expr   = ParseExpression("stream = 'orders' AND schema_name = 'OrderCreated'");
        var result = ExpressionFilterParser.Analyze(expr);

        // First predicate (stream) goes to server, second goes to client
        result.HasServerFilter.ShouldBeTrue();
        result.HasClientFilter.ShouldBeTrue();
        // Server scope should be StreamName (first encountered)
        result.ServerScope.ShouldBe(FilterScope.StreamName);
    }

    #endregion

    #region Edge Cases

    [Test]
    public void empty_expression_returns_no_filters() {
        // Create a simple expression that doesn't match any server patterns
        var expr   = ParseExpression("position > 0");
        var result = ExpressionFilterParser.Analyze(expr);

        result.HasServerFilter.ShouldBeFalse();
        result.HasClientFilter.ShouldBeTrue();
    }

    [Test]
    public void escapes_special_regex_characters() {
        var expr   = ParseExpression("stream = 'orders.v1'");
        var result = ExpressionFilterParser.Analyze(expr);

        // The dot should be escaped in the regex
        result.ServerRegex.ShouldBe(@"^orders\.v1$");
    }

    [Test]
    public void multiple_same_scope_predicates_combine() {
        // When we have multiple stream predicates in AND, they combine
        var expr   = ParseExpression("stream LIKE 'order%' AND stream LIKE '%created'");
        var result = ExpressionFilterParser.Analyze(expr);

        // Both predicates can go to server and should be combined
        result.HasServerFilter.ShouldBeTrue();
        result.ServerScope.ShouldBe(FilterScope.StreamName);
        // Regex should be an alternation of both patterns
        result.ServerRegex.ShouldNotBeNull();
        result.ServerRegex!.ShouldContain("order");
        result.ServerRegex.ShouldContain("created");
    }

    #endregion

    #region RecordFilter Integration

    [Test]
    public void expression_filter_extracts_target_stream() {
        var filter = RecordFilter.ByExpression("stream = 'orders' AND properties.amount > 100");

        filter.TargetStream.Value.ShouldBe("orders");
        filter.Expression.ShouldBe("stream = 'orders' AND properties.amount > 100");
    }

    [Test]
    public void expression_filter_matches_records_correctly() {
        var filter = RecordFilter.ByExpression("stream = 'orders' AND timestamp >= '2024-01-01'");

        var matching = new Record {
            Id             = Guid.NewGuid(),
            Stream         = "orders",
            Position       = 0,
            StreamRevision = 0,
            Timestamp = new DateTime(
                2024, 6, 15,
                0, 0, 0,
                DateTimeKind.Utc
            ),
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
        };

        var nonMatching = new Record {
            Id             = Guid.NewGuid(),
            Stream         = "orders",
            Position       = 0,
            StreamRevision = 0,
            Timestamp = new DateTime(
                2023, 6, 15,
                0, 0, 0,
                DateTimeKind.Utc
            ),
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
        };

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void stream_filter_matches_stream_patterns() {
        var filter = RecordFilter.ByStreamPrefix("order");

        var matching = new Record {
            Id             = Guid.NewGuid(),
            Stream         = "order-123",
            Position       = 0,
            StreamRevision = 0,
            Timestamp      = DateTime.UtcNow,
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
        };

        var nonMatching = new Record {
            Id             = Guid.NewGuid(),
            Stream         = "payment-123",
            Position       = 0,
            StreamRevision = 0,
            Timestamp      = DateTime.UtcNow,
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
        };

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void expression_filter_with_client_predicate_matches() {
        var filter = RecordFilter.ByExpression("stream = 'orders' AND properties.amount > 100");

        var matching = new Record {
            Id             = Guid.NewGuid(),
            Stream         = "orders",
            Position       = 0,
            StreamRevision = 0,
            Timestamp      = DateTime.UtcNow,
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
                .With("amount", 150)
        };

        var nonMatching = new Record {
            Id             = Guid.NewGuid(),
            Stream         = "orders",
            Position       = 0,
            StreamRevision = 0,
            Timestamp      = DateTime.UtcNow,
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
                .With("amount", 50)
        };

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    #endregion

    IFilterExpression ParseExpression(string input) {
        var result = _parser.Parse(input);
        result.IsSuccess.ShouldBeTrue($"Failed to parse: {input}");
        return result.Value;
    }
}
