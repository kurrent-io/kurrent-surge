using Kurrent.Testkit.TUnit;
using static Kurrent.Surge.Codecs.Avro.Generators.Tests.Unit.AvroGeneratorHarness;
using static VerifyTUnit.Verifier;

namespace Kurrent.Surge.Codecs.Avro.Generators.Tests.Unit;

/// <summary>
/// The complete generated output for a schema, under each set of options, committed as a snapshot.
/// </summary>
/// <remarks>
/// Substring assertions only see what someone thought to look for, and they go stale silently: this
/// suite had tests asserting a type name in code that did not compile, and tests that passed because
/// the string they searched for happened to appear in embedded schema text rather than where they
/// meant. A snapshot asserts everything, so any change to the output is a reviewable diff.
/// <para>
/// To accept an intended change, replace the <c>.verified.txt</c> with the <c>.received.txt</c> the
/// failing run writes beside it, and review the diff before committing.
/// </para>
/// </remarks>
[Category("Codecs")]
[Timeout(30_000)]
public class GeneratorSnapshotTests {
    /// <summary>
    /// Small enough to read as a diff, wide enough to show the shapes each option changes: a record, a
    /// nested record, a collection, an enum, a plain fixed, a union, and the logical types the time
    /// model and nanosecond options move.
    /// </summary>
    const string Schema =
        """
        {
          "type": "record",
          "name": "Order",
          "namespace": "Acme.Messages",
          "doc": "An order placed by a customer.",
          "fields": [
            { "name": "OrderId",  "type": "string", "doc": "The customer's reference." },
            { "name": "Customer", "type": { "type": "record", "name": "Customer",
                                            "fields": [ { "name": "Name", "type": "string" } ] } },
            { "name": "Lines",    "type": { "type": "array", "items": "Customer" } },
            { "name": "Status",   "type": { "type": "enum", "name": "State", "symbols": ["Open", "Closed"] } },
            { "name": "Digest",   "type": { "type": "fixed", "name": "Md5", "size": 16 } },
            { "name": "Note",     "type": ["null", "string"] },
            { "name": "Total",    "type": { "type": "bytes", "logicalType": "decimal", "precision": 12, "scale": 2 } },
            { "name": "Day",      "type": { "type": "int",  "logicalType": "date" } },
            { "name": "Placed",   "type": { "type": "long", "logicalType": "timestamp-millis" } },
            { "name": "Precise",  "type": { "type": "long", "logicalType": "timestamp-nanos" } }
          ]
        }
        """;

    #region ->> Test Case Generators <<-

    public sealed class OptionCases : TestCaseDataGenerator<(string Name, Dictionary<string, string> Properties)> {
        protected override IEnumerable<TestCase<(string, Dictionary<string, string>)>> Data() {
            yield return new(("Defaults", []), DisplayName: "defaults");

            yield return new(("ApacheInterop", Property("KurrentAvroApacheInterop", "On")),
                DisplayName: "Apache interop on");

            yield return new(("Internal", Property("KurrentAvroAccessModifier", "internal")),
                DisplayName: "internal types");

            yield return new(("XmlDocs", Property("KurrentAvroXmlDocs", "On")),
                DisplayName: "XML docs on");

            yield return new(("NodaTime", Property("KurrentAvroTimeModel", "NodaTime")),
                DisplayName: "NodaTime time model");

            yield return new(("NanoInstant", Property("KurrentAvroNanosecondTimestamps", "NanoInstant")),
                DisplayName: "NanoInstant nanoseconds");
        }
    }

    #endregion // Test Case Generators

    [Test]
    [OptionCases]
    public async Task generated_output(string name, Dictionary<string, string> properties) {
        // Arrange & Act
        var driver = DriveWith(properties, ("/schemas/Order.avsc", Schema));

        // Assert — the whole output, not a chosen slice of it.
        await Verify(driver).UseParameters(name);
    }
}
