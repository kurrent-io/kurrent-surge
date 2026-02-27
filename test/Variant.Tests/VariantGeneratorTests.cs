
using Kurrent.Variant.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Kurrent.Variant.Tests;

public class VariantGeneratorTests {
    // [Test]
    public async Task should_generate_expected_code() {
        // Create a test compilation
        var source = """
            using Kurrent.Variant;
            public readonly partial record struct ReplicatorVariant : IVariant<string, int> { }
            """;
        var compilation = CreateCompilation(source);
        var generator   = new VariantGenerator();

        // Run the generator
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(compilation);

        // Get the generated source
        var result          = driver.GetRunResult();
        var generatedSource = result.GeneratedTrees.First().ToString();

        // Assert expected patterns exist
        await Assert.That(generatedSource).Contains("public bool IsString");
        await Assert.That(generatedSource).Contains("public string AsString");
        await Assert.That(generatedSource).Contains("public bool IsInt");
    }

    static Compilation CreateCompilation(string source) =>
        CSharpCompilation.Create(
            "compilation",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(System.Reflection.Binder).GetTypeInfo().Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
        );
}
