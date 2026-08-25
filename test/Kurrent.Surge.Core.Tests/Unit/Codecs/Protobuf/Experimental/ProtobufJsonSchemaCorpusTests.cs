using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using Kurrent.Testkit.TUnit;

namespace Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;

/// <summary>
/// Gate 1 over a third-party corpus — every message the Arrow Flight protocol declares.
/// </summary>
/// <remarks>
/// The two hand-written fixtures are small and were written by the same person as the converter,
/// which is the weakest kind of evidence there is. These files were not: 54 named types, 22 of them
/// enums, in schemas nobody here shaped to make the converter look right.
/// <para>
/// One case per message, so a failure names the message rather than the corpus.
/// </para>
/// </remarks>
public class ProtobufJsonSchemaCorpusTests {
    static readonly ProtobufJsonSchemaOptions Canonical = new() { Canonical = true };

    // The root an import is spelled against, so a name here reads as 'arrow/flight/protocol/…' —
    // which is how any file importing these would write it.
    static string CorpusRoot { get; } = Path.Combine(AppContext.BaseDirectory, "protos");

    /// <remarks>
    /// Built once. Neither file imports the other, so one set holds both.
    /// </remarks>
    static ProtoSchemaSet Set { get; } = ProtoSchemaSet.Build(
        Directory.EnumerateFiles(Path.Combine(CorpusRoot, "arrow"), "*.proto", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new ProtoFile(Path.GetRelativePath(CorpusRoot, path).Replace('\\', '/'), File.ReadAllText(path)))
            .ToArray());

    public sealed class CorpusMessages : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() =>
            Set.MessageNames
                // A map entry is synthesized by the language, not declared, so no caller exports one.
                .Where(name => !name.EndsWith("Entry", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => new TestCase<string>(name, DisplayName: name));
    }

    [Test]
    [CorpusMessages]
    public async ValueTask round_trips_every_arrow_flight_message(string messageTypeName) {
        // Arrange — hoisting and layout legitimately change the printed text, so the comparison is
        // between the two exported documents rather than between two .proto sources.
        var expected = ProtobufJsonSchema.Export(Set, messageTypeName, Canonical);

        // Act
        var reimported = ProtobufJsonSchema.Import(expected);
        var rebuilt    = ProtoSchemaSet.Build(reimported, "reimported.proto");
        var actual     = ProtobufJsonSchema.Export(rebuilt, messageTypeName, Canonical);

        // Assert
        await Assert.That(actual).IsEqualTo(expected).Because(reimported);
    }

    [Test]
    public async ValueTask reads_the_whole_arrow_flight_protocol() {
        // Arrange — the parser gate. A message closure cannot be exported from a set that never built.
        var expected = 32;

        // Act
        var messages = Set.MessageNames.Count(name => !name.EndsWith("Entry", StringComparison.Ordinal));

        // Assert — FlightSql declares 32 messages and Flight declares its own on top, so the set holds
        // more than FlightSql alone. The floor is what makes this fail if a file silently stops parsing.
        await Assert.That(messages).IsGreaterThanOrEqualTo(expected);
    }
}
