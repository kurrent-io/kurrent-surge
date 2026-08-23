using System.Text.RegularExpressions;
using Kurrent.Surge.RecordFilters.Tests.Targets;

namespace Kurrent.Surge.RecordFilters.Tests;

// A constraint is a superset the caller may act on. Reporting less than the expression accepts would
// lose records, so the cases that report nothing matter as much as the ones that report something.
[Category("RecordFilters")]
public class ConstraintTests {
    #region ->> Test Case Generators <<-

    public sealed class ConstrainedCases : TestCaseDataGenerator<(string Expression, string Column, FilterTermKind Kind, string[] Values, bool IsExact)> {
        protected override IEnumerable<TestCase<(string Expression, string Column, FilterTermKind Kind, string[] Values, bool IsExact)>> Data() {
            yield return new(("stream = 'orders'",                      "stream",   FilterTermKind.Equals, ["orders"],      true),  "equality is one value");
            yield return new(("order.stream = 'orders'",                "stream",   FilterTermKind.Equals, ["orders"],      true),  "qualifier is stripped first");
            yield return new(("field_stream = 'orders'",                "stream",   FilterTermKind.Equals, ["orders"],      true),  "trim prefix is stripped");
            yield return new(("stream = 'a.b'",                         "stream",   FilterTermKind.Equals, ["a.b"],         true),  "a literal is not escaped");
            yield return new(("stream = 'orders' AND amount > 100",     "stream",   FilterTermKind.Equals, ["orders"],      false), "an unconstrained conjunct leaves the caller to trim");
            yield return new(("stream LIKE 'ord%'",                     "stream",   FilterTermKind.Like,   ["ord%"],        true),  "LIKE keeps its own pattern");
            yield return new(("stream ILIKE 'ord%'",                    "stream",   FilterTermKind.Like,   ["ord%"],        true),  "ILIKE differs only by its flag");
            yield return new(("stream IN ('a', 'b')",                   "stream",   FilterTermKind.AnyOf,  ["a", "b"],      true),  "IN is one alternative of many values");
            yield return new(("revision = '5'",                         "revision", FilterTermKind.Equals, ["5"],           true),  "an alias resolves to its column");
            yield return new(("status = 'shipped'",                     "status",   FilterTermKind.Equals, ["shipped"],     true),  "any declared column can be asked about");
        }
    }

    public sealed class UnconstrainedCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("amount > 100",                            DisplayName: "says nothing about stream");
            yield return new("props['region'] = 'us-east'",             DisplayName: "a different column entirely");
            yield return new("stream['x'] = 'a'",                       DisplayName: "a key reads a member, not the column");
            yield return new("stream != 'a'",                           DisplayName: "inequality names no value");
            yield return new("stream > 'a'",                            DisplayName: "ordering names no value");
            yield return new("stream IS NULL",                          DisplayName: "IS NULL names no value");
            yield return new("stream = 123",                            DisplayName: "a non-string literal");
            yield return new("NOT (stream = 'a')",                      DisplayName: "negation inverts the set");
            yield return new("stream NOT LIKE 'a%'",                    DisplayName: "negated LIKE inverts the set");
            yield return new("stream NOT IN ('a')",                     DisplayName: "negated IN inverts the set");
            yield return new("stream = 'a' OR amount > 100",            DisplayName: "one unconstrained branch poisons the union");
            yield return new("stream = 'a' OR (stream = 'b' AND amount > 1)", DisplayName: "a compound branch poisons the union");
        }
    }

    public sealed class RegexCases : TestCaseDataGenerator<(string Expression, string Regex)> {
        protected override IEnumerable<TestCase<(string Expression, string Regex)>> Data() {
            yield return new(("stream = 'orders'",       "^orders$"),      "equality anchors the literal");
            yield return new(("stream = 'a.b'",          @"^a\.b$"),       "equality escapes the literal");
            yield return new(("stream LIKE 'ord%'",      "^ord.*$"),       "percent is any run");
            yield return new(("stream LIKE 'a_c'",       "^a.c$"),         "underscore is one character");
            yield return new(("stream IN ('a', 'b')",    "^(a|b)$"),       "IN is one alternation");
        }
    }

    #endregion

    #region ->> Reporting <<-

    [Test]
    [ConstrainedCases]
    public async ValueTask reports_what_an_expression_constrains_a_column_to(
        string expression, string column, FilterTermKind kind, string[] values, bool isExact
    ) {
        // Arrange
        var expected = new FilterConstraint([new(kind, values, CaseInsensitive: false)], isExact);
        var filter   = OrderSchema.Default.Parse(expression);

        // Act
        var constraint = filter.ConstraintOn(column);

        // Assert — terms compare by value, so the whole constraint is one assertion
        await Assert.That(constraint.IsExact).IsEqualTo(expected.IsExact);
        await Assert.That(constraint.Terms.Count).IsEqualTo(1);
        await Assert.That(constraint.Terms[0].Kind).IsEqualTo(kind);
        await Assert.That(constraint.Terms[0].Values).IsEquivalentTo(values);
    }

    [Test]
    [UnconstrainedCases]
    public async ValueTask reports_nothing_when_it_cannot_widen_safely(string expression) {
        // Arrange
        var filter = OrderSchema.Default.Parse(expression);

        // Act
        var constraint = filter.ConstraintOn("stream");

        // Assert
        await Assert.That(constraint).IsEqualTo(FilterConstraint.None);
        await Assert.That(constraint.Any).IsFalse();
    }

    [Test]
    public async ValueTask reports_nothing_for_a_column_the_schema_does_not_declare() {
        // Arrange
        var filter = OrderSchema.Default.Parse("stream = 'orders'");

        // Act
        var constraint = filter.ConstraintOn("nonexistent");

        // Assert
        await Assert.That(constraint).IsEqualTo(FilterConstraint.None);
    }

    [Test]
    public async ValueTask reports_every_branch_of_a_covered_or() {
        // Arrange
        var filter = OrderSchema.Default.Parse("stream = 'a' OR stream = 'b'");

        // Act
        var constraint = filter.ConstraintOn("stream");

        // Assert — alternatives, and together they are the whole expression
        await Assert.That(constraint.Terms.Count).IsEqualTo(2);
        await Assert.That(constraint.IsExact).IsTrue();
    }

    [Test]
    public async ValueTask reports_case_insensitivity_without_applying_it() {
        // Arrange
        var filter = OrderSchema.Default.Parse("stream ILIKE 'ORD%'");

        // Act
        var term = filter.ConstraintOn("stream").Terms[0];

        // Assert — the flag is reported so a caller combining terms decides where it goes
        await Assert.That(term.CaseInsensitive).IsTrue();
        await Assert.That(term.ToRegex()).IsEqualTo("^ORD.*$");
    }

    #endregion

    #region ->> Regular Expressions <<-

    [Test]
    [RegexCases]
    public async ValueTask renders_a_term_as_an_anchored_regex(string expression, string expected) {
        // Arrange
        var term = OrderSchema.Default.Parse(expression).ConstraintOn("stream").Terms[0];

        // Act
        var pattern = term.ToRegex();

        // Assert
        await Assert.That(pattern).IsEqualTo(expected);
    }

    #endregion

    #region ->> The Guarantee <<-

    [Test]
    public async ValueTask a_constraint_accepts_everything_the_filter_accepts() {
        // Arrange — the guarantee the whole mechanism rests on: over-select is fine, under-select is not
        var filter     = OrderSchema.Default.Parse("stream LIKE 'orders-%' AND status = 'shipped'");
        var constraint = filter.ConstraintOn("stream");
        var accepted   = new Regex(constraint.Terms[0].ToRegex());

        var matching    = NewOrder("orders-1", "shipped");
        var trimmedAway = NewOrder("orders-2", "pending");
        var outside     = NewOrder("invoices-1", "shipped");

        // Act & Assert — everything the filter accepts, the constraint also accepts
        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(accepted.IsMatch(matching.Stream)).IsTrue();

        // The constraint over-selects, and IsExact says so
        await Assert.That(filter.IsMatch(trimmedAway)).IsFalse();
        await Assert.That(accepted.IsMatch(trimmedAway.Stream)).IsTrue();
        await Assert.That(constraint.IsExact).IsFalse();

        // What neither accepts
        await Assert.That(filter.IsMatch(outside)).IsFalse();
        await Assert.That(accepted.IsMatch(outside.Stream)).IsFalse();
    }

    #endregion

    static Order NewOrder(string stream, string status) =>
        new() {
            Stream    = stream,
            Revision  = 7,
            Amount    = 150m,
            CreatedAt = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero),
            Status    = status
        };
}
