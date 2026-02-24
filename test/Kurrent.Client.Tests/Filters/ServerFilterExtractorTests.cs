using Kurrent.Client.Streams;
using Kurrent.Testing.TUnit;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class ServerFilterExtractorTests {
    readonly ExpressionFilterParser _parser = new();

    #region Stream Filter Extraction

    public readonly record struct StreamExtractionTestCase(string Description, string Expression, IRecordFilter ExpectedFilter) {
        public override string ToString() => Description;
    }

    public class StreamExtractionTestCases : TestCaseGenerator<StreamExtractionTestCase> {
        // Realistic stream names following KurrentDB convention: {Category}-{Id}
        const string GamesStream    = "Games-D0332EB7480643E481BF3205296958FD";
        const string OrdersStream   = "Orders-A1B2C3D4E5F6789012345678ABCDEF01";
        const string PaymentsStream = "Payments-F9E8D7C6B5A4321098765432FEDCBA98";

        protected override IEnumerable<StreamExtractionTestCase> Data() {
            // Exact stream match - targets specific stream
            yield return new(
                "Exact stream match",
                $"stream = '{GamesStream}'",
                RecordFilter.ByStream(GamesStream)
            );

            // LIKE without wildcards - equivalent to exact match
            yield return new(
                "LIKE exact match (no wildcards)",
                $"stream LIKE '{OrdersStream}'",
                RecordFilter.ByStream(OrdersStream)
            );

            // Multiple streams - reads from $all with filter
            yield return new(
                "Multiple streams (IN)",
                $"stream IN ('{OrdersStream}', '{PaymentsStream}')",
                RecordFilter.ByStream(OrdersStream, PaymentsStream)
            );

            // Category prefix - common pattern for filtering by aggregate type
            yield return new(
                "Category prefix (Games-%)",
                "stream LIKE 'Games-%'",
                RecordFilter.ByStreamPattern(@"^Games-.*$")
            );

            // Category prefix - Orders
            yield return new(
                "Category prefix (Orders-%)",
                "stream LIKE 'Orders-%'",
                RecordFilter.ByStreamPattern(@"^Orders-.*$")
            );

            // Single char wildcard - matches exactly one character
            yield return new(
                "Single char wildcard",
                "stream LIKE 'Games-_'",
                RecordFilter.ByStreamPattern(@"^Games-.$")
            );

            // Case-insensitive category prefix
            yield return new(
                "Case-insensitive category prefix",
                "stream ILIKE 'GAMES-%'",
                RecordFilter.ByStreamPattern(@"(?i)^GAMES-.*$")
            );

            // Case-insensitive exact match - no target stream (could match multiple variants)
            yield return new(
                "Case-insensitive exact match",
                $"stream ILIKE '{GamesStream}'",
                RecordFilter.ByStreamPattern($"(?i)^{GamesStream}$")
            );
        }
    }

    [Test]
    [StreamExtractionTestCases]
    public void extracts_stream_filter(StreamExtractionTestCase testcase) {
        var expr   = ParseExpression(testcase.Expression);
        var filter = ServerFilterExtractor.Extract(expr);

        filter
            .ShouldBeOfType<StreamFilter>()
            .ShouldBeEquivalentTo(testcase.ExpectedFilter);
    }

    [Test]
    public void extracts_target_stream_from_equality() {
        var expr   = ParseExpression("stream = 'Orders-A1B2C3D4E5F6'");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.ShouldBeOfType<StreamFilter>();
        filter.TargetStream.ShouldBe(StreamName.From("Orders-A1B2C3D4E5F6"));
        filter.Expression.ShouldBe("^Orders-A1B2C3D4E5F6$");
    }

    [Test]
    public void no_target_stream_for_category_prefix() {
        var expr   = ParseExpression("stream LIKE 'Orders-%'");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.TargetStream.ShouldBe(StreamName.All);
        filter.ShouldBeOfType<StreamFilter>();
    }

    [Test]
    public void no_target_stream_for_in_expression() {
        var expr   = ParseExpression("stream IN ('Orders-123', 'Payments-456')");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.TargetStream.ShouldBe(StreamName.All);
        filter.ShouldBeOfType<StreamFilter>();
        filter.Expression.ShouldBe("^(Orders-123|Payments-456)$");
    }

    #endregion

    #region Server-Side Stream Filters

    [Test]
    public void converts_stream_equality_to_exact_regex() {
        var expr   = ParseExpression("stream = 'Games-ABC123'");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.Expression.ShouldBe("^Games-ABC123$");
        filter.ShouldBeOfType<StreamFilter>();
    }

    [Test]
    public void converts_category_prefix_to_regex() {
        var expr   = ParseExpression("stream LIKE 'Orders-%'");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.Expression.ShouldBe("^Orders-.*$");
        filter.ShouldBeOfType<StreamFilter>();
    }

    [Test]
    public void converts_stream_like_suffix_to_regex() {
        var expr   = ParseExpression("stream LIKE '%-completed'");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.Expression.ShouldBe("^.*-completed$");
        filter.ShouldBeOfType<StreamFilter>();
    }

    [Test]
    public void converts_stream_like_contains_to_regex() {
        var expr   = ParseExpression("stream LIKE '%order%'");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.Expression.ShouldBe("^.*order.*$");
        filter.ShouldBeOfType<StreamFilter>();
    }

    [Test]
    public void converts_stream_in_to_alternation_regex() {
        var expr   = ParseExpression("stream IN ('Orders-001', 'Payments-002', 'Refunds-003')");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.Expression.ShouldBe("^(Orders-001|Payments-002|Refunds-003)$");
        filter.ShouldBeOfType<StreamFilter>();
    }

    #endregion

    #region Server-Side Schema Filters

    [Test]
    public void converts_schema_name_equality_to_exact_regex() {
        var expr   = ParseExpression("schema_name = 'OrderCreated'");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.Expression.ShouldBe("^OrderCreated$");
        filter.ShouldBeOfType<SchemaFilter>();
    }

    [Test]
    public void converts_schema_alias_to_exact_regex() {
        var expr   = ParseExpression("schema = 'OrderCreated'");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.Expression.ShouldBe("^OrderCreated$");
        filter.ShouldBeOfType<SchemaFilter>();
    }

    [Test]
    public void converts_schema_like_to_regex() {
        var expr   = ParseExpression("schema_name LIKE 'Order%'");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.Expression.ShouldBe("^Order.*$");
        filter.ShouldBeOfType<SchemaFilter>();
    }

    [Test]
    public void converts_schema_in_to_alternation_regex() {
        var expr   = ParseExpression("schema_name IN ('OrderCreated', 'OrderUpdated')");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.Expression.ShouldBe("^(OrderCreated|OrderUpdated)$");
        filter.ShouldBeOfType<SchemaFilter>();
    }

    #endregion

    #region Client-Side Filters (no server filter)

    [Test]
    public void timestamp_filter_has_no_server_filter() {
        var expr   = ParseExpression("timestamp >= '2024-01-01'");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.ShouldBe(RecordFilter.None);
    }

    [Test]
    public void position_filter_has_no_server_filter() {
        var expr   = ParseExpression("position > 1000");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.ShouldBe(RecordFilter.None);
    }

    [Test]
    public void properties_filter_has_no_server_filter() {
        var expr   = ParseExpression("properties.amount > 100");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.ShouldBe(RecordFilter.None);
    }

    [Test]
    public void revision_filter_has_no_server_filter() {
        var expr   = ParseExpression("stream_revision > 10");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.ShouldBe(RecordFilter.None);
    }

    #endregion

    #region Combined Server and Client Filters

    [Test]
    public void extracts_server_filter_from_combined_expression() {
        var expr   = ParseExpression("stream = 'Orders-ABC123' AND timestamp >= '2024-01-01'");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.ShouldBeOfType<StreamFilter>();
        filter.Expression.ShouldBe("^Orders-ABC123$");
        filter.TargetStream.ShouldBe(StreamName.From("Orders-ABC123"));
    }

    [Test]
    public void extracts_server_filter_with_properties() {
        var expr   = ParseExpression("stream = 'Orders-ABC123' AND properties.amount > 100");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.ShouldBeOfType<StreamFilter>();
        filter.Expression.ShouldBe("^Orders-ABC123$");
    }

    [Test]
    public void extracts_schema_filter_with_properties() {
        var expr   = ParseExpression("schema_name = 'OrderCreated' AND properties.userId = 'Users-123'");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.ShouldBeOfType<SchemaFilter>();
    }

    [Test]
    public void extracts_server_filter_with_multiple_client_predicates() {
        var expr   = ParseExpression("stream = 'Orders-ABC123' AND properties.amount > 100 AND timestamp >= '2024-01-01'");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.ShouldBeOfType<StreamFilter>();
    }

    #endregion

    #region Negated Expressions

    [Test]
    public void negated_stream_filter_has_no_server_filter() {
        var expr   = ParseExpression("stream != 'Test-123'");
        var filter = ServerFilterExtractor.Extract(expr);

        // Negated comparisons cannot be lifted to server
        filter.ShouldBe(RecordFilter.None);
    }

    [Test]
    public void negated_like_has_no_server_filter() {
        var expr   = ParseExpression("stream NOT LIKE 'Test-%'");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.ShouldBe(RecordFilter.None);
    }

    [Test]
    public void negated_in_has_no_server_filter() {
        var expr   = ParseExpression("stream NOT IN ('Test-001', 'Demo-002')");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.ShouldBe(RecordFilter.None);
    }

    #endregion

    #region OR Expressions

    [Test]
    public void or_expression_has_no_server_filter() {
        var expr   = ParseExpression("stream = 'Orders-001' OR stream = 'Payments-002'");
        var filter = ServerFilterExtractor.Extract(expr);

        // OR expressions cannot be lifted to server as a single AND-flattened predicate
        filter.ShouldBe(RecordFilter.None);
    }

    #endregion

    #region Mixed Scope Filters

    [Test]
    public void stream_and_schema_filters_uses_stream_scope() {
        // When we have both stream and schema_name filters, stream takes priority
        var expr   = ParseExpression("stream = 'Orders-ABC123' AND schema_name = 'OrderCreated'");
        var filter = ServerFilterExtractor.Extract(expr);

        // Stream predicate goes to server (priority), schema is handled client-side
        filter.ShouldBeOfType<StreamFilter>();
    }

    #endregion

    #region Edge Cases

    [Test]
    public void client_only_expression_has_no_server_filter() {
        // Create a simple expression that doesn't match any server patterns
        var expr   = ParseExpression("position > 0");
        var filter = ServerFilterExtractor.Extract(expr);

        filter.ShouldBe(RecordFilter.None);
    }

    [Test]
    public void escapes_special_regex_characters() {
        var expr   = ParseExpression("stream = 'Orders.v1-ABC123'");
        var filter = ServerFilterExtractor.Extract(expr);

        // The dot should be escaped in the regex, but '-' doesn't need escaping
        filter.Expression.ShouldBe(@"^Orders\.v1-ABC123$");
    }

    [Test]
    public void multiple_category_predicates_combine() {
        // When we have multiple stream predicates in AND, they combine
        var expr   = ParseExpression("stream LIKE 'Orders-%' AND stream LIKE '%-completed'");
        var filter = ServerFilterExtractor.Extract(expr);

        // Both predicates can go to server and should be combined
        filter.ShouldBeOfType<StreamFilter>();
        // Regex should be an alternation of both patterns
        filter.Expression.ShouldNotBeNull();
        filter.Expression.ShouldContain("Orders");
        filter.Expression.ShouldContain("completed");
    }

    #endregion

    #region RecordFilter Integration

    [Test]
    public void expression_filter_extracts_target_stream() {
        var filter = RecordFilter.ByExpression("stream = 'Orders-ABC123' AND properties.amount > 100");

        filter.TargetStream.Value.ShouldBe("Orders-ABC123");
        filter.Expression.ShouldBe("stream = 'Orders-ABC123' AND properties.amount > 100");
    }

    [Test]
    public void expression_filter_matches_records_correctly() {
        var filter = RecordFilter.ByExpression("stream = 'Orders-ABC123' AND timestamp >= '2024-01-01'");

        var matching = new Record {
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                Stream         = StreamName.From("Orders-ABC123"),
                Revision = Revision.From(0)
            },
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
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                Stream         = StreamName.From("Orders-ABC123"),
                Revision = Revision.From(0)
            },
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
    public void stream_filter_matches_category_prefix() {
        var filter = RecordFilter.ByStreamPrefix("Orders-");

        var matching = new Record {
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                Stream         = StreamName.From("Orders-D0332EB7480643E4"),
                Revision = Revision.From(0)
            },
            Timestamp = DateTime.UtcNow,
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
        };

        var nonMatching = new Record {
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                Stream         = StreamName.From("Payments-F9E8D7C6B5A43210"),
                Revision = Revision.From(0)
            },
            Timestamp = DateTime.UtcNow,
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
        };

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void expression_filter_with_client_predicate_matches() {
        var filter = RecordFilter.ByExpression("stream = 'Orders-ABC123' AND properties.amount > 100");

        var matching = new Record {
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                Stream         = StreamName.From("Orders-ABC123"),
                Revision = Revision.From(0)
            },
            Timestamp = DateTime.UtcNow,
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
                .With("amount", 150)
        };

        var nonMatching = new Record {
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                Stream         = StreamName.From("Orders-ABC123"),
                Revision = Revision.From(0)
            },
            Timestamp = DateTime.UtcNow,
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
