using Kurrent.Surge.Client.Streams;
using Serilog;

namespace Kurrent.Surge.Tests.Filters;

[Category("Filters")]
public class StreamNameFilterTests {
    readonly RecordFaker _faker = new();

    const string OrdersStream   = "Orders-D0332EB7480643E481BF3205296958FD";
    const string PaymentsStream = "Payments-A1B2C3D4E5F6789012345678ABCDEF01";

    #region ->> Test Case Generators <<-

    public class ValidInputCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("orders",                   DisplayName: "simple name");
            yield return new("Orders-ABC123",            DisplayName: "category-id format");
            yield return new("$ce-orders",               DisplayName: "projected stream");
            yield return new("my.stream.with.dots-001",  DisplayName: "dots in name");
        }
    }

    public class InvalidInputCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("",                    DisplayName: "empty");
            yield return new(" ",                   DisplayName: "whitespace");
            yield return new("$all",                DisplayName: "all stream");
            yield return new("$idx-ce-orders",      DisplayName: "category index");
            yield return new("$idx-et-OrderCreated", DisplayName: "schema index");
            yield return new("$idx-user-my-index",  DisplayName: "user index");
        }
    }

    #endregion

    #region ->> Parse <<-

    [Test]
    [ValidInputCases]
    public async ValueTask parse_returns_filter_with_target_stream(string input) {
        // Act
        var filter = StreamNameFilter.Parse(input);

        // Assert
        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From(input.Trim()));
        await Assert.That(filter.Expression).IsEqualTo(input.Trim());
    }

    [Test]
    [InvalidInputCases]
    public async ValueTask parse_throws_on_invalid_input(string input) {
        await Assert.That(() => StreamNameFilter.Parse(input)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [InvalidInputCases]
    public async ValueTask try_parse_returns_false_on_invalid_input(string input) {
        // Act
        var result = StreamNameFilter.TryParse(input, out var filter, out var error);

        Log.Information("TryParse rejected '{Input}': {Error}", input, error);

        // Assert
        await Assert.That(result).IsFalse();
        await Assert.That(filter).IsNull();
    }

    #endregion

    #region ->> IsMatch <<-

    [Test]
    public async ValueTask is_match_returns_true_for_exact_stream() {
        var filter = StreamNameFilter.Parse(OrdersStream);

        await Assert.That(filter.IsMatch(_faker.With(OrdersStream))).IsTrue();
    }

    [Test]
    public async ValueTask is_match_returns_false_for_different_stream() {
        var filter = StreamNameFilter.Parse(OrdersStream);

        await Assert.That(filter.IsMatch(_faker.With(PaymentsStream))).IsFalse();
    }

    #endregion
}
