using Kurrent.Surge.Schema.Naming;

namespace Kurrent.Surge.Tests.SchemaRegistry.NameStrategies;

public class NamingTests {
    public class TitleCaseCases : TestCaseDataGenerator<(string Input, string Expected)> {
        protected override IEnumerable<TestCase<(string Input, string Expected)>> Data() {
            yield return new(("live-transfer-processor",  "Live Transfer Processor"), DisplayName: "kebab-case");
            yield return new(("live-transfers",           "Live Transfers"),          DisplayName: "two words");
            yield return new(("live_transfer_processor",  "Live Transfer Processor"), DisplayName: "snake_case");
            yield return new(("live transfer\tprocessor", "Live Transfer Processor"), DisplayName: "whitespace");
            yield return new(("LiveTransferProcessor",    "Live Transfer Processor"), DisplayName: "PascalCase");
            yield return new(("liveTransferProcessor",    "Live Transfer Processor"), DisplayName: "camelCase");
            yield return new(("HTTPServer",               "HTTP Server"),             DisplayName: "acronym");
            yield return new(("-live--transfers_",        "Live Transfers"),          DisplayName: "repeated and edge separators");
            yield return new(("live",                     "Live"),                    DisplayName: "one word");
        }
    }

    [Test]
    [TitleCaseCases]
    public async ValueTask to_title_case_capitalises_each_word_and_joins_them_with_spaces(string input, string expected) {
        // Act
        var actual = input.ToTitleCase();

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }
}
