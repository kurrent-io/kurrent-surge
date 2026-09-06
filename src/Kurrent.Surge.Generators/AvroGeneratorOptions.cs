using Microsoft.CodeAnalysis.Diagnostics;

namespace Kurrent.Surge.Codecs.Avro.Generators;

enum AvroAccessModifier { Public, Internal }

enum AvroXmlDocs { Off, On }

/// <summary>The CLR type a <c>timestamp-nanos</c> field surfaces as.</summary>
/// <remarks>
/// <c>Long</c> is what Apache does — it sees an unknown logical type over a <c>long</c> and erases it.
/// <c>NanoInstant</c> goes beyond Apache; the richer type is converted back at the
/// <c>ISpecificRecord</c> boundary so Apache's own readers and writers keep working either way.
/// </remarks>
enum AvroNanosecondTimestamps { Long, NanoInstant }

/// <summary>The family of types the temporal logical types surface as.</summary>
/// <remarks>
/// Both models draw the same distinction the Avro specification does, between a point on the global
/// timeline and a reading with no zone attached. <c>Bcl</c> spells it <c>DateTimeOffset</c> against
/// <c>DateOnly</c>, <c>TimeOnly</c> and <c>DateTime</c>; <c>NodaTime</c> spells it <c>Instant</c> against
/// <c>LocalDate</c>, <c>LocalTime</c> and <c>LocalDateTime</c>.
/// <para>
/// Only <c>NodaTime</c> can carry <c>duration</c>, whose months-days-milliseconds triple has no BCL
/// equivalent, and it makes <c>NanoInstant</c> redundant because <c>Instant</c> is nanosecond-native.
/// </para>
/// </remarks>
enum AvroTimeModel { Bcl, NodaTime }

/// <summary>Whether generated types carry the surface Apache.Avro's own readers and writers need.</summary>
/// <remarks>
/// <c>Off</c> emits nothing Apache-shaped: no <c>ISpecificRecord</c>, no <c>Schema</c> property, no
/// <c>SpecificFixed</c> base, and our own <c>AvroDecimal</c>. The wire bytes are identical either way —
/// Avro's contract is the encoding, not the CLR shape — so a consumer of any language reads the output
/// the same.
/// <para>
/// <c>On</c> is for handing generated types to Apache's own machinery. It costs the
/// <c>Schema</c> property, and reading that property roots <c>Avro.Schema.Parse</c>, which pulls
/// Newtonsoft.Json into the binary — measured at about 9.5 MB.
/// </para>
/// </remarks>
enum AvroApacheInterop { Off, On }

/// <summary>
/// The generator's build-time settings, read from MSBuild properties.
/// </summary>
/// <remarks>
/// A record struct rather than a class: this is a pipeline value, and the incremental cache compares
/// pipeline values by equality. A class here would compare by reference and re-emit on every keystroke.
/// </remarks>
readonly record struct AvroGeneratorOptions(
    AvroAccessModifier       AccessModifier,
    AvroXmlDocs              XmlDocs,
    AvroNanosecondTimestamps NanosecondTimestamps,
    AvroTimeModel            TimeModel,
    AvroApacheInterop        ApacheInterop,
    EquatableArray<string>   Invalid) {
    public static AvroGeneratorOptions Default =>
        new(AvroAccessModifier.Public, AvroXmlDocs.Off, AvroNanosecondTimestamps.Long, AvroTimeModel.Bcl,
            AvroApacheInterop.Off, EquatableArray<string>.Empty);

    public bool EmitsApacheInterop => ApacheInterop is AvroApacheInterop.On;

    public string AccessModifierKeyword => AccessModifier is AvroAccessModifier.Internal ? "internal" : "public";

    public bool EmitsXmlDocs => XmlDocs is AvroXmlDocs.On;

    public bool UsesNodaTime => TimeModel is AvroTimeModel.NodaTime;

    /// <remarks>
    /// Under the NodaTime model <c>Instant</c> is already nanosecond-native, so the richer type is
    /// whatever that model names — the switch only chooses between the erased primitive and the rich type.
    /// </remarks>
    public bool EmitsRichNanoseconds => NanosecondTimestamps is AvroNanosecondTimestamps.NanoInstant;

    public static AvroGeneratorOptions From(AnalyzerConfigOptions options) {
        var invalid = new List<string>();

        return new AvroGeneratorOptions(
            Read(options, "KurrentAvroAccessModifier", AvroAccessModifier.Public, invalid),
            Read(options, "KurrentAvroXmlDocs", AvroXmlDocs.Off, invalid),
            Read(options, "KurrentAvroNanosecondTimestamps", AvroNanosecondTimestamps.Long, invalid),
            Read(options, "KurrentAvroTimeModel", AvroTimeModel.Bcl, invalid),
            Read(options, "KurrentAvroApacheInterop", AvroApacheInterop.Off, invalid),
            new EquatableArray<string>(invalid.ToArray()));
    }

    /// <remarks>
    /// An unparseable value is reported rather than silently defaulted. A typo in
    /// <c>KurrentAvroAccessModifier</c> would otherwise leave the generated types public, which is the
    /// opposite of what the consumer asked for and invisible until someone reads the generated code.
    /// </remarks>
    static T Read<T>(AnalyzerConfigOptions options, string property, T fallback, List<string> invalid)
        where T : struct, Enum {
        if (!options.TryGetValue($"build_property.{property}", out var text) || string.IsNullOrWhiteSpace(text))
            return fallback;

        if (Enum.TryParse<T>(text.Trim(), ignoreCase: true, out var value) && Enum.IsDefined(typeof(T), value))
            return value;

        invalid.Add($"{property}={text.Trim()}|{string.Join(", ", Enum.GetNames(typeof(T)))}");

        return fallback;
    }
}
