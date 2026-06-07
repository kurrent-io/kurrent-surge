using Kurrent.CodeAnalysis.Matching;
using Microsoft.CodeAnalysis;

namespace Kurrent.CodeAnalysis.Tests.Unit;

public class TypePatternTests {
    static TypeSymbol Make(string ns, string name, params TypeSymbol[] args) =>
        new(
            Name: name,
            Namespace: ns,
            FullName: string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}",
            Kind: TypeKind.Class,
            Traits: TypeTraits.Public,
            Accessibility: Accessibility.Public,
            Special: SpecialType.None,
            TypeArguments: args,
            BaseType: null,
            ArrayElementType: null,
            Interfaces: [],
            Attributes: [],
            ContainingTypes: []);

    [Test]
    public async ValueTask non_generic_exact_match() {
        var pattern = TypePattern.Parse("Ns.Foo");
        await Assert.That(pattern.Matches(Make("Ns", "Foo"))).IsTrue();
        await Assert.That(pattern.Matches(Make("Ns", "Bar"))).IsFalse();
        await Assert.That(pattern.Matches(Make("Other", "Foo"))).IsFalse();
    }

    [Test]
    public async ValueTask open_generic_by_arity() {
        var pattern = TypePattern.Parse("Ns.Foo<,>");
        var arg1    = Make("", "A");
        var arg2    = Make("", "B");

        await Assert
            .That(
                pattern.Matches(
                    Make(
                        "Ns", "Foo", arg1,
                        arg2)))
            .IsTrue();

        await Assert.That(pattern.Matches(Make("Ns", "Foo", arg1))).IsFalse();

        await Assert
            .That(
                pattern.Matches(
                    Make(
                        "Ns", "Foo", arg1,
                        arg2, arg1)))
            .IsFalse();
    }

    [Test]
    public async ValueTask closed_generic_recursive_match() {
        var pattern   = TypePattern.Parse("Ns.Foo<System.String>");
        var stringArg = Make("System", "String");
        var intArg    = Make("System", "Int32");
        await Assert.That(pattern.Matches(Make("Ns", "Foo", stringArg))).IsTrue();
        await Assert.That(pattern.Matches(Make("Ns", "Foo", intArg))).IsFalse();
    }

    [Test]
    public async ValueTask primitive_aliases_canonicalize() {
        var intPattern    = TypePattern.Parse("int");
        var stringPattern = TypePattern.Parse("string");

        await Assert.That(intPattern.Matches(Make("System", "Int32"))).IsTrue();
        await Assert.That(stringPattern.Matches(Make("System", "String"))).IsTrue();
    }

    [Test]
    public async ValueTask alias_inside_closed_generic_matches() {
        var pattern   = TypePattern.Parse("Ns.Foo<int, string>");
        var intArg    = Make("System", "Int32");
        var stringArg = Make("System", "String");

        await Assert
            .That(
                pattern.Matches(
                    Make(
                        "Ns", "Foo", intArg,
                        stringArg)))
            .IsTrue();
    }

    [Test]
    public async ValueTask unqualified_non_primitive_name_throws() {
        await Assert.That(() => TypePattern.Parse("Foo")).Throws<ArgumentException>();
    }

    [Test]
    public async ValueTask empty_pattern_throws() {
        await Assert.That(() => TypePattern.Parse("")).Throws<ArgumentException>();
    }

    [Test]
    public async ValueTask pattern_cache_returns_same_instance() {
        var first  = TypePattern.Parse("Ns.Foo<,>");
        var second = TypePattern.Parse("Ns.Foo<,>");
        await Assert.That(first.Arity).IsEqualTo(second.Arity);
        await Assert.That(first.Name).IsEqualTo(second.Name);
    }
}