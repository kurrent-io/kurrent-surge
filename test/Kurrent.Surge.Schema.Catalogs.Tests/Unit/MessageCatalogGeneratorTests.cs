using System.Collections.Immutable;
using Kurrent.Surge.Schema.Catalogs.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kurrent.Surge.Schema.Catalogs.Tests.Unit;

/// <summary>
/// Drives <see cref="MessageCatalogGenerator"/> directly over hand-built compilations to prove the
/// client-coupled emission gate: a contracts-only compilation (no <c>KurrentClientMessageOptions</c>)
/// must get the catalog + provider but NOT the <c>Use*Catalog()</c> extensions, while a compilation
/// that has the client type must get them. The physical samples project
/// (Kurrent.Surge.Schema.Catalogs.Tests.Samples) covers the real-build side of the same guarantee.
/// </summary>
[Category("Schema")]
public class MessageCatalogGeneratorTests {
    // A namespace-scan catalog declaration that drives the generator. No Kurrent.Surge.Client anywhere.
    const string ContractsSource =
        """
        using Kurrent.Surge.Schema.Catalogs;

        [assembly: MessageCatalog("Home.Devices", Key = "Devices")]

        namespace Home.Devices;

        public record DevicePaired(string DeviceId);
        public record DeviceRemoved(string DeviceId);
        """;

    // Minimal stand-in for the client type the generated Use*Catalog() extensions extend. Its mere
    // presence in the compilation is what the generator's emission gate keys on.
    const string ClientStub =
        """
        namespace Kurrent.Surge.Client;

        public class KurrentClientMessageOptions {
            public KurrentClientMessageOptions UseCatalog(System.Collections.Generic.IEnumerable<global::Kurrent.Surge.Schema.Catalogs.MessageCatalog> catalogs) => this;
        }
        """;

    [Test]
    public async ValueTask emits_catalog_and_provider_in_a_contracts_only_compilation() {
        // Arrange + Act
        var generated = RunGenerator(ContractsSource);

        // Assert — the catalog aggregate and the per-assembly provider attribute are emitted.
        await Assert.That(generated).Contains("class MessageCatalogs");
        await Assert.That(generated).Contains("MessageCatalogProviderAttribute");
        await Assert.That(generated).Contains("Devices");
    }

    [Test]
    public async ValueTask omits_use_catalog_extensions_when_no_client_is_referenced() {
        // Arrange + Act — the compilation has no KurrentClientMessageOptions, so the gate stays shut.
        var generated = RunGenerator(ContractsSource);

        // Assert — nothing client-coupled is emitted; a contracts assembly would otherwise fail to build.
        await Assert.That(generated).DoesNotContain("KurrentClientMessageOptions");
        await Assert.That(generated).DoesNotContain("UseDevicesCatalog");
    }

    [Test]
    public async ValueTask emits_use_catalog_extensions_when_the_client_is_referenced() {
        // Arrange + Act — the client options type is present, so the gate opens.
        var generated = RunGenerator(ContractsSource, ClientStub);

        // Assert — the Use*Catalog() extensions are emitted against the client options type.
        await Assert.That(generated).Contains("UseDevicesCatalog");
        await Assert.That(generated).Contains("KurrentClientMessageOptions");
    }

    [Test]
    public async ValueTask generates_catalog_in_the_contracts_only_samples_assembly() {
        // Arrange — climate events the namespace-scan catalog should have collected in the real
        // contracts-only Kurrent.Surge.Schema.Catalogs.Tests.Samples assembly (references no Kurrent.Surge.Client).
        var temperatureMeasured = typeof(Samples.SmartHome.Climate.TemperatureMeasured);
        var humidityMeasured    = typeof(Samples.SmartHome.Climate.HumidityMeasured);

        // Act
        var generatedTypes = Samples.GenerationProof.Climate.Types;

        // Assert
        await Assert.That(generatedTypes).Contains(temperatureMeasured);
        await Assert.That(generatedTypes).Contains(humidityMeasured);
    }

    // Runs the generator over the given sources and returns the concatenated generated source text.
    static string RunGenerator(params string[] sources) {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        // Ensure the package assembly (MessageCatalogAttribute, MessageCatalog) is referenced even if
        // it has not been touched (and thus loaded) by the test AppDomain yet.
        references.Add(MetadataReference.CreateFromFile(typeof(MessageCatalog).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "ContractsCompilation",
            sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new MessageCatalogGenerator())
            .RunGenerators(compilation);

        return string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
    }
}
