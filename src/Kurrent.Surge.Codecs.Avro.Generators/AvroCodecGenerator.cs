using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Kurrent.Surge.Codecs.Avro.Generators.Emission;
using Kurrent.Surge.Codecs.Avro.Generators.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Kurrent.Surge.Codecs.Avro.Generators;

/// <summary>Names for the pipeline steps, so incremental caching can be asserted in tests.</summary>
static class TrackingNames {
    public const string Schemas = "AvroSchemas";
    public const string Output  = "AvroGeneratedOutput";
}

/// <summary>A diagnostic carried through the pipeline as data, so the model stays equatable.</summary>
/// <remarks>
/// <see cref="Diagnostic"/> itself is not a safe pipeline value: it holds a <see cref="Location"/> and
/// does not give the element-wise equality the incremental cache needs. The descriptor is re-attached in
/// the output action.
/// </remarks>
readonly record struct SchemaDiagnostic(string Id, string Detail, string Path, int Start, int Line, int Character) {
    public Diagnostic ToDiagnostic(DiagnosticDescriptor descriptor) {
        var position = new LinePosition(Line, Character);

        return Diagnostic.Create(
            descriptor,
            Location.Create(Path, new TextSpan(Start, 1), new LinePositionSpan(position, position)),
            Path,
            Detail);
    }
}

/// <summary>One generated C# type: a record, an enum, a fixed, or a union's variant interface.</summary>
readonly record struct TypeEmission(string FullName, string Source);

/// <summary>The complete result of reading one .avsc file: everything the output action needs.</summary>
/// <param name="NeedsUuidLogicalType">
/// True when the schema declares <c>uuid</c> on a <c>fixed</c>. Apache's own uuid validation rejects that
/// base, so the schema cannot be parsed until the replacement logical type is registered.
/// </param>
readonly record struct SchemaEmission(
    string                            SchemaPath,
    string                            CodecSource,
    string                            RootTypeName,
    string                            CodecClassName,
    bool                              NeedsUuidLogicalType,
    EquatableArray<TypeEmission>      Types,
    EquatableArray<SchemaDiagnostic>  Diagnostics) {
    public bool HasCodec => CodecSource.Length > 0;
}

[Generator(LanguageNames.CSharp)]
public sealed class AvroCodecGenerator : IIncrementalGenerator {
    const string SchemaExtension = ".avsc";

    static readonly DiagnosticDescriptor InvalidSchema = new(
        id: "KAVRO001",
        title: "Avro schema could not be parsed",
        messageFormat: "'{0}' could not be parsed as an Avro schema: {1}",
        category: "Kurrent.Avro",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor UnsupportedSchema = new(
        id: "KAVRO002",
        title: "Avro schema uses a construct the generated codec does not support",
        messageFormat: "'{0}' uses an unsupported construct: {1}",
        category: "Kurrent.Avro",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor DuplicateType = new(
        id: "KAVRO003",
        title: "Two Avro schemas declare the same type",
        messageFormat: "'{0}' declares '{1}', which another schema also declares; only the first declaration is generated",
        category: "Kurrent.Avro",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        // Each schema is parsed and emitted exactly once. The pipeline value is the emitted TEXT rather
        // than the parsed model: strings are equatable, the model is not, so this is what lets the
        // incremental cache work at all.
        var emissions = context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith(SchemaExtension, StringComparison.OrdinalIgnoreCase))
            .Select(static (text, cancellationToken) => Build(text, cancellationToken))
            .WithTrackingName(TrackingNames.Schemas);

        // A single collected output action, because a type declared in one schema and referenced from
        // another must be emitted once. Per-file output would emit it twice and break the compilation.
        context.RegisterSourceOutput(
            emissions.Collect().WithTrackingName(TrackingNames.Output),
            static (context, emissions) => Emit(context, emissions));
    }

    static SchemaEmission Build(AdditionalText text, CancellationToken cancellationToken) {
        var sourceText = text.GetText(cancellationToken);
        var content    = sourceText?.ToString() ?? "";

        if (!AvroSchemaParser.TryParse(content, out var document, out var error, out var offset))
            return Failed(text, sourceText, "KAVRO001", error, offset);

        if (document.Root is not AvroRecord root)
            return Failed(text, sourceText, "KAVRO002", "the root schema is not a record", 0);

        return new SchemaEmission(
            SchemaPath:           text.Path,
            CodecSource:          new AvroCodecEmitter(document).Emit(root),
            RootTypeName:         AvroNaming.QualifiedTypeName(root),
            CodecClassName:       AvroCodecEmitter.CodecClassName(root),
            NeedsUuidLogicalType: document.NamedTypes.Values.OfType<AvroFixed>().Any(f => f.Logical is AvroLogicalKind.Uuid),
            Types:                new EquatableArray<TypeEmission>(new AvroTypeEmitter(document).Emit().ToArray()),
            Diagnostics:          EquatableArray<SchemaDiagnostic>.Empty);
    }

    static SchemaEmission Failed(AdditionalText text, SourceText? sourceText, string id, string detail, int offset) =>
        new(
            SchemaPath:           text.Path,
            CodecSource:          "",
            RootTypeName:         "",
            CodecClassName:       "",
            NeedsUuidLogicalType: false,
            Types:                EquatableArray<TypeEmission>.Empty,
            Diagnostics:          new EquatableArray<SchemaDiagnostic>([Diagnostic(text, sourceText, id, detail, offset)]));

    static SchemaDiagnostic Diagnostic(AdditionalText text, SourceText? sourceText, string id, string detail, int offset) {
        var position = sourceText is not null && offset < sourceText.Length
            ? sourceText.Lines.GetLinePosition(offset)
            : new LinePosition(0, 0);

        return new SchemaDiagnostic(id, detail, text.Path, offset, position.Line, position.Character);
    }

    static void Emit(SourceProductionContext context, ImmutableArray<SchemaEmission> emissions) {
        foreach (var emission in emissions)
        foreach (var diagnostic in emission.Diagnostics)
            context.ReportDiagnostic(diagnostic.ToDiagnostic(diagnostic.Id == "KAVRO001" ? InvalidSchema : UnsupportedSchema));

        EmitTypes(context, emissions);

        // Two schemas with the same root record produce the same codec class. The conflict is already
        // reported against the duplicated type, and adding the second source would throw.
        var codecs = emissions
            .Where(static emission => emission.HasCodec)
            .GroupBy(static emission => emission.CodecClassName)
            .Select(static group => group.First())
            .ToList();

        foreach (var emission in codecs) context.AddSource($"{emission.CodecClassName}.g.cs", emission.CodecSource);

        if (codecs.Count > 0) EmitRegistry(context, codecs);
    }

    static void EmitTypes(SourceProductionContext context, ImmutableArray<SchemaEmission> emissions) {
        var emitted = new Dictionary<string, string>();

        foreach (var emission in emissions)
        foreach (var type in emission.Types) {
            if (emitted.TryGetValue(type.FullName, out var first)) {
                // Identical redeclarations are common when schemas share a nested type, and harmless:
                // the same C# comes out either way. Only a genuine divergence is worth reporting.
                if (first != type.Source)
                    context.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(
                        DuplicateType, Location.None, emission.SchemaPath, type.FullName));

                continue;
            }

            emitted[type.FullName] = type.Source;

            context.AddSource($"{AvroNaming.HintName(type.FullName)}.g.cs", type.Source);
        }
    }

    static void EmitRegistry(SourceProductionContext context, IReadOnlyList<SchemaEmission> emissions) {
        var writer = new CodeWriter();

        writer.Lines(Templates.FileHeader);
        writer.Line();

        using (writer.Block("internal static class AvroGeneratedCodecRegistration")) {
            writer.Line("[global::System.Runtime.CompilerServices.ModuleInitializer]");

            using (writer.Block("internal static void Register()")) {
                // Emitted only when a schema in this compilation needs it: the registration replaces
                // Apache's 'uuid' logical type for the whole process, so an assembly that declares no
                // such schema does not pay for it.
                if (emissions.Any(static emission => emission.NeedsUuidLogicalType)) {
                    writer.Line("global::Kurrent.Surge.Codecs.Avro.UuidLogicalType.Enable();");
                    writer.Line();
                }

                foreach (var emission in emissions) {
                    writer.Lines(
                        $"""
                         global::Kurrent.Surge.Codecs.Avro.AvroCodecRegistry.Register(
                             typeof({emission.RootTypeName}),
                             static (value, encoder) => {emission.CodecClassName}.Encode(({emission.RootTypeName})value, encoder),
                             static decoder => {emission.CodecClassName}.Decode(decoder));
                         """);
                }
            }
        }

        context.AddSource("AvroGeneratedCodecRegistration.g.cs", writer.ToString());
    }
}
