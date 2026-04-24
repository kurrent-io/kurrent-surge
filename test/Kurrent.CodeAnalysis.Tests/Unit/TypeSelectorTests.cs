using Kurrent.CodeAnalysis.Extraction;
using Kurrent.CodeAnalysis.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kurrent.CodeAnalysis.Tests.Unit;

public class TypeSelectorTests {
    static List<TypeSymbol> ExtractAll(string source) {
        var tree = CSharpSyntaxTree.ParseText(source);

        var refs = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var compilation = CSharpCompilation.Create(
            "Test",
            [tree],
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var named = compilation.GetSymbolsWithName(_ => true).OfType<INamedTypeSymbol>().ToList();
        return named.Select(s => s.ToTypeSymbol()).ToList();
    }

    [Test]
    public async ValueTask kind_filter_accepts_union() {
        var selector = new TypeSelector().OfKind(TypeKind.Record, TypeKind.Class);

        var infos = ExtractAll(
            """
            namespace Ns {
                public class A { }
                public record R { }
                public struct S { }
            }
            """);

        var matched = infos.Where(selector.Evaluate).Select(i => i.Name).ToList();
        await Assert.That(matched).Contains("A");
        await Assert.That(matched).Contains("R");
        await Assert.That(matched).DoesNotContain("S");
    }

    [Test]
    public async ValueTask traits_filter_requires_intersection() {
        var selector = new TypeSelector().Is(TypeTraits.Partial | TypeTraits.Public);

        var infos = ExtractAll(
            """
            namespace Ns {
                public partial class A { }
                internal partial class B { }
                public class C { }
            }
            """);

        var matched = infos.Where(selector.Evaluate).Select(i => i.Name).ToList();
        await Assert.That(matched).Contains("A");
        await Assert.That(matched).DoesNotContain("B");
        await Assert.That(matched).DoesNotContain("C");
    }

    [Test]
    public async ValueTask implementing_pattern_matches_transitive_interfaces() {
        var selector = new TypeSelector().Implementing("Ns.IFoo<,>");

        var infos = ExtractAll(
            """
            namespace Ns {
                public interface IFoo<T, U> { }
                public class Bar : IFoo<int, string> { }
                public class Baz { }
            }
            """);

        var matched = infos.Where(selector.Evaluate).Select(i => i.Name).ToList();
        await Assert.That(matched).Contains("Bar");
        await Assert.That(matched).DoesNotContain("Baz");
    }

    [Test]
    public async ValueTask deriving_from_matches_base_type() {
        var selector = new TypeSelector().DerivingFrom("Ns.Base");

        var infos = ExtractAll(
            """
            namespace Ns {
                public class Base { }
                public class Derived : Base { }
                public class Standalone { }
            }
            """);

        var matched = infos.Where(selector.Evaluate).Select(i => i.Name).ToList();
        await Assert.That(matched).Contains("Derived");
        await Assert.That(matched).DoesNotContain("Standalone");
    }

    [Test]
    public async ValueTask matching_predicate_filters_by_arbitrary_condition() {
        var selector = new TypeSelector().Matching(t => t.Name.StartsWith("Xyz"));

        var infos = ExtractAll(
            """
            namespace Ns {
                public class XyzOne { }
                public class XyzTwo { }
                public class Other { }
            }
            """);

        var matched = infos.Where(selector.Evaluate).Select(i => i.Name).ToList();
        await Assert.That(matched.Count).IsEqualTo(2);
        await Assert.That(matched).Contains("XyzOne");
        await Assert.That(matched).Contains("XyzTwo");
    }

    [Test]
    public async ValueTask chained_filters_compose_with_and() {
        var selector = new TypeSelector()
            .OfKind(TypeKind.Record)
            .Is(TypeTraits.Partial)
            .Implementing("Ns.IMarker");

        var infos = ExtractAll(
            """
            namespace Ns {
                public interface IMarker { }
                public partial record Hit : IMarker { }
                public partial record MissNoMarker { }
                public record MissNotPartial : IMarker { }
                public partial class MissNotRecord : IMarker { }
            }
            """);

        var matched = infos.Where(selector.Evaluate).Select(i => i.Name).ToList();
        await Assert.That(matched).Contains("Hit");
        await Assert.That(matched).DoesNotContain("MissNoMarker");
        await Assert.That(matched).DoesNotContain("MissNotPartial");
        await Assert.That(matched).DoesNotContain("MissNotRecord");
    }
}