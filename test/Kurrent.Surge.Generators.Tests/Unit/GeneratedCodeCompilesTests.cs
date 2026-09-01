using Kurrent.Testkit.TUnit;
using static Kurrent.Surge.Codecs.Avro.Generators.Tests.Unit.AvroGeneratorHarness;

namespace Kurrent.Surge.Codecs.Avro.Generators.Tests.Unit;

/// <summary>
/// Compiles what the generator emits, for every combination of options, from a schema using every
/// construct it supports.
/// </summary>
/// <remarks>
/// The black-box contract: a schema and a set of options go in, code that builds comes out. Assertions
/// on emitted text cannot express this — a type can be referenced and never emitted, and every substring
/// the test looks for is still present. That defect shipped once and only a real compilation caught it.
/// <para>
/// Wire behaviour is not asserted here. Round-trip and Apache parity live in
/// <c>Kurrent.Surge.Codecs.Avro.Tests</c>, which compiles against these types for real.
/// </para>
/// </remarks>
[Category("Codecs")]
[Timeout(30_000)]
public class GeneratedCodeCompilesTests {
    /// <summary>Every construct the generator supports, in one schema.</summary>
    const string EverythingSchema =
        """
        {
          "type": "record",
          "name": "Everything",
          "namespace": "Acme.Messages",
          "doc": "One of each.",
          "fields": [
            { "name": "Text",     "type": "string", "doc": "A documented field." },
            { "name": "Count",    "type": "int" },
            { "name": "Ratio",    "type": "double" },
            { "name": "Blob",     "type": "bytes" },
            { "name": "Nested",   "type": { "type": "record", "name": "Inner",
                                            "fields": [ { "name": "Value", "type": "long" } ] } },
            { "name": "Items",    "type": { "type": "array", "items": "Inner" } },
            { "name": "Lookup",   "type": { "type": "map", "values": "int" } },
            { "name": "Status",   "type": { "type": "enum", "name": "State", "symbols": ["On", "Off"] } },
            { "name": "Raw",      "type": { "type": "fixed", "name": "Raw16", "size": 16 },
                                  "default": "0123456789ABCDEF" },
            { "name": "Maybe",    "type": ["null", "string"] },
            { "name": "Either",   "type": [
                                    { "type": "record", "name": "Cat", "fields": [ { "name": "Lives", "type": "int" } ] },
                                    { "type": "record", "name": "Dog", "fields": [ { "name": "Bark", "type": "string" } ] } ] },
            { "name": "Id",       "type": { "type": "string", "logicalType": "uuid" } },
            { "name": "Money",    "type": { "type": "bytes", "logicalType": "decimal", "precision": 12, "scale": 2 } },
            { "name": "Day",      "type": { "type": "int",  "logicalType": "date" } },
            { "name": "Clock",    "type": { "type": "int",  "logicalType": "time-millis" } },
            { "name": "Stamp",    "type": { "type": "long", "logicalType": "timestamp-millis" } },
            { "name": "Wall",     "type": { "type": "long", "logicalType": "local-timestamp-millis" } },
            { "name": "Precise",  "type": { "type": "long", "logicalType": "timestamp-nanos" } },
            { "name": "FixedId",  "type": { "type": "fixed", "name": "Uuid16", "size": 16, "logicalType": "uuid" } },
            { "name": "FixedSum", "type": { "type": "fixed", "name": "Dec8",   "size": 8,
                                            "logicalType": "decimal", "precision": 12, "scale": 2 } },
            { "name": "Window",   "type": { "type": "fixed", "name": "Span12", "size": 12, "logicalType": "duration" } }
          ]
        }
        """;

    #region ->> Test Case Generators <<-

    /// <remarks>
    /// Not the full Cartesian product. Each case turns on the options that interact — the time model
    /// decides whether <c>duration</c> and the nanosecond type are modelled, and interop decides whether
    /// the Apache surface and Apache's decimal appear. The rest are independent of one another.
    /// </remarks>
    public sealed class OptionCases : TestCaseDataGenerator<(string Name, Dictionary<string, string> Properties)> {
        protected override IEnumerable<TestCase<(string, Dictionary<string, string>)>> Data() {
            yield return new(("defaults", []), DisplayName: "defaults");

            yield return new(("interop", Property("KurrentAvroApacheInterop", "On")),
                DisplayName: "Apache interop on");

            yield return new(("internal", Property("KurrentAvroAccessModifier", "internal")),
                DisplayName: "internal types");

            yield return new(("docs", Property("KurrentAvroXmlDocs", "On")),
                DisplayName: "XML docs on");

            yield return new(("nanoinstant", Property("KurrentAvroNanosecondTimestamps", "NanoInstant")),
                DisplayName: "NanoInstant nanoseconds");

            yield return new(("nodatime", Property("KurrentAvroTimeModel", "NodaTime")),
                DisplayName: "NodaTime time model");

            yield return new(("nodatime+interop", Combine(
                    Property("KurrentAvroTimeModel", "NodaTime"),
                    Property("KurrentAvroApacheInterop", "On"))),
                DisplayName: "NodaTime with Apache interop");

            yield return new(("nodatime+instant", Combine(
                    Property("KurrentAvroTimeModel", "NodaTime"),
                    Property("KurrentAvroNanosecondTimestamps", "NanoInstant"))),
                DisplayName: "NodaTime with Instant nanoseconds");
        }

        static Dictionary<string, string> Combine(params Dictionary<string, string>[] parts) =>
            parts.SelectMany(p => p).ToDictionary(p => p.Key, p => p.Value);
    }

    #endregion // Test Case Generators

    [Test]
    [OptionCases]
    public async ValueTask generated_code_compiles(string name, Dictionary<string, string> properties) {
        // Arrange & Act
        var result = RunWith(properties, ("/schemas/Everything.avsc", EverythingSchema));

        // Assert — no generator diagnostics, and the emitted code builds.
        var errors = CompileErrors(result);

        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(errors.Select(e => $"{e.Id}: {e.GetMessage()}")).IsEmpty();
        await Assert.That(result.GeneratedTrees).IsNotEmpty();
    }
}
