using Kurrent.CodeAnalysis.Extraction;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kurrent.CodeAnalysis.Tests.Unit;

public class TypeSymbolExtractorTests {
    static INamedTypeSymbol Compile(string source, string typeName) {
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

        var symbol = compilation.GetSymbolsWithName(s => s == typeName).OfType<INamedTypeSymbol>().First();
        return symbol;
    }

    [Test]
    public async ValueTask extracts_name_namespace_fullname() {
        var symbol = Compile("namespace Foo.Bar { public partial class Baz { } }", "Baz");
        var info   = symbol.ToTypeSymbol();

        await Assert.That(info.Name).IsEqualTo("Baz");
        await Assert.That(info.Namespace).IsEqualTo("Foo.Bar");
        await Assert.That(info.FullName).IsEqualTo("Foo.Bar.Baz");
    }

    [Test]
    public async ValueTask extracts_traits_from_modifiers_and_accessibility() {
        var symbol = Compile("namespace Ns { internal sealed partial class A { } }", "A");
        var info   = symbol.ToTypeSymbol();

        await Assert.That(info.IsInternal).IsTrue();
        await Assert.That(info.IsSealed).IsTrue();
        await Assert.That(info.IsPartial).IsTrue();
    }

    [Test]
    public async ValueTask extracts_record_kind() {
        var symbol = Compile("namespace Ns { public partial record R(int X); }", "R");
        var info   = symbol.ToTypeSymbol();

        await Assert.That(info.Kind).IsEqualTo(TypeKind.Record);
    }

    [Test]
    public async ValueTask sorts_interfaces_by_fullname_ordinally() {
        var source = """
            namespace Ns {
                public interface IZeta { }
                public interface IAlpha { }
                public interface IMiddle { }
                public class Concrete : IZeta, IAlpha, IMiddle { }
            }
            """;

        var symbol = Compile(source, "Concrete");
        var info   = symbol.ToTypeSymbol();

        var names = info.Interfaces.Select(i => i.FullName).ToArray();
        await Assert.That(names).IsEquivalentTo(["Ns.IAlpha", "Ns.IMiddle", "Ns.IZeta"]);
    }

    [Test]
    public async ValueTask extracts_base_type_when_present() {
        var source = """
            namespace Ns {
                public class Base { }
                public class Derived : Base { }
            }
            """;

        var symbol = Compile(source, "Derived");
        var info   = symbol.ToTypeSymbol();

        await Assert.That(info.BaseType).IsNotNull();
        await Assert.That(info.BaseType!.FullName).IsEqualTo("Ns.Base");
    }

    [Test]
    public async ValueTask base_type_null_when_it_is_object() {
        var symbol = Compile("namespace Ns { public class A { } }", "A");
        var info   = symbol.ToTypeSymbol();

        await Assert.That(info.BaseType).IsNull();
    }

    [Test]
    public async ValueTask containing_types_outer_to_inner_with_partial_forced() {
        var source = """
            namespace Ns {
                public static class Outer {
                    public class Middle {
                        public class Leaf { }
                    }
                }
            }
            """;

        var symbol = Compile(source, "Leaf");
        var info   = symbol.ToTypeSymbol();

        await Assert.That(info.ContainingTypes.Length).IsEqualTo(2);
        await Assert.That(info.ContainingTypes[0].Name).IsEqualTo("Outer");
        await Assert.That(info.ContainingTypes[1].Name).IsEqualTo("Middle");
        await Assert.That(info.ContainingTypes[0].Traits.HasFlag(TypeTraits.Partial)).IsTrue();
        await Assert.That(info.ContainingTypes[1].Traits.HasFlag(TypeTraits.Partial)).IsTrue();
    }

    [Test]
    public async ValueTask implements_query_uses_interface_list() {
        var source = """
            namespace Ns {
                public interface IFoo<T> { }
                public class Bar : IFoo<int> { }
            }
            """;

        var symbol = Compile(source, "Bar");
        var info   = symbol.ToTypeSymbol();

        await Assert.That(info.Implements("Ns.IFoo<>")).IsTrue();
        await Assert.That(info.Implements("Ns.IFoo<int>")).IsTrue();
        await Assert.That(info.Implements("Ns.IFoo<string>")).IsFalse();
    }

    [Test]
    public async ValueTask derives_from_query_uses_base_type() {
        var source = """
            namespace Ns {
                public class Base { }
                public class Derived : Base { }
            }
            """;

        var symbol = Compile(source, "Derived");
        var info   = symbol.ToTypeSymbol();

        await Assert.That(info.DerivesFrom("Ns.Base")).IsTrue();
        await Assert.That(info.DerivesFrom("Ns.Other")).IsFalse();
    }

    [Test]
    public async ValueTask extracts_attributes() {
        var source = """
            namespace Ns {
                public class MyAttr : System.Attribute { public MyAttr(string v) { } }
                [MyAttr("hello")]
                public class Tagged { }
            }
            """;

        var symbol = Compile(source, "Tagged");
        var info   = symbol.ToTypeSymbol();

        await Assert.That(info.HasAttribute("Ns.MyAttr")).IsTrue();
        var attr = info.Attribute("Ns.MyAttr");
        await Assert.That(attr).IsNotNull();
        await Assert.That(attr!.ConstructorArgs.Length).IsEqualTo(1);
        await Assert.That((attr.ConstructorArgs[0] as ConstantValue.Primitive)?.AsString).IsEqualTo("hello");
    }

    [Test]
    public async ValueTask special_kind_detects_nullable_of_t_via_original_definition() {
        var source = """
            namespace Ns {
                public class Holder {
                    public int? Nullable { get; set; }
                }
            }
            """;

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

        var holder       = compilation.GetSymbolsWithName(s => s == "Holder").OfType<INamedTypeSymbol>().First();
        var propertyType = holder.GetMembers("Nullable").OfType<IPropertySymbol>().First().Type;

        var info = propertyType.ToTypeSymbol();
        await Assert.That(info.Special).IsEqualTo(SpecialType.System_Nullable_T);
        await Assert.That(info.IsNullableOfT).IsTrue();
        await Assert.That(info.TypeArguments.Length).IsEqualTo(1);
        await Assert.That(info.TypeArguments[0].Special).IsEqualTo(SpecialType.System_Int32);
    }

    [Test]
    public async ValueTask array_element_type_extracted() {
        var source = """
            namespace Ns {
                public class Holder {
                    public int[] Numbers { get; set; } = new int[0];
                }
            }
            """;

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

        var holder       = compilation.GetSymbolsWithName(s => s == "Holder").OfType<INamedTypeSymbol>().First();
        var propertyType = holder.GetMembers("Numbers").OfType<IPropertySymbol>().First().Type;

        var info = propertyType.ToTypeSymbol();
        await Assert.That(info.Kind).IsEqualTo(TypeKind.Array);
        await Assert.That(info.IsArray).IsTrue();
        await Assert.That(info.ArrayElementType).IsNotNull();
        await Assert.That(info.ArrayElementType!.Special).IsEqualTo(SpecialType.System_Int32);
    }

    [Test]
    public async ValueTask extracts_accessibility_and_values_are_equatable() {
        var a     = Compile("namespace Ns { public class A { } }", "A");
        var info1 = a.ToTypeSymbol();
        var info2 = a.ToTypeSymbol();

        // Two extractions of the same symbol produce equal TypeSymbol values — equality based on value fields
        await Assert.That(info1).IsEqualTo(info2);
        await Assert.That(info1.GetHashCode()).IsEqualTo(info2.GetHashCode());

        // Accessibility is captured as a cache-safe value — no live symbol retained
        await Assert.That(info1.Accessibility).IsEqualTo(Accessibility.Public);
    }
}