// ReSharper disable ConvertToLocalFunction
// ReSharper disable PropertyCanBeMadeInitOnly.Local
// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable NotAccessedPositionalProperty.Local

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Kurrent.Testkit.Tests;

[Category("Testing")]
public class ObjectGraphEquivalencyTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    #region ->> Primitive & value type equivalence <<-

    [Test]
    public async ValueTask equivalent_integers_pass() {
        var value = Faker.Random.Int();

        await Assert.That(value).IsDeepEquivalentTo(value);
    }

    [Test]
    public async ValueTask different_integers_fail() {
        var actual   = Faker.Random.Int(1, 1000);
        var expected = actual + 1;

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask equivalent_strings_pass() {
        var value = Faker.Lorem.Word();

        await Assert.That(value).IsDeepEquivalentTo(value);
    }

    [Test]
    public async ValueTask different_strings_fail() {
        var actual   = Faker.Lorem.Word();
        var expected = $"{Faker.Lorem.Word()}_different";

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask equivalent_booleans_pass() {
        var value = Faker.Random.Bool();

        await Assert.That(value).IsDeepEquivalentTo(value);
    }

    [Test]
    public async ValueTask equivalent_guids_pass() {
        var guid = Faker.Random.Guid();

        await Assert.That(guid).IsDeepEquivalentTo(guid);
    }

    [Test]
    public async ValueTask equivalent_datetimes_pass() {
        var dt = Faker.Date.Recent();

        await Assert.That(dt).IsDeepEquivalentTo(dt);
    }

    #endregion

    #region ->> Null handling <<-

    [Test]
    [SuppressMessage("Usage", "TUnitAssertions0005:Assert.That(...) should not be used with a constant value")]
    public async ValueTask both_null_passes() {
        await Assert.That(null).IsDeepEquivalentTo(null);
    }

    [Test]
    [SuppressMessage("Usage", "TUnitAssertions0005:Assert.That(...) should not be used with a constant value")]
    public async ValueTask actual_null_expected_non_null_fails() {
        var expected = Faker.Lorem.Sentence();

        var action = async () => await Assert.That(null).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask actual_non_null_expected_null_fails() {
        var actual = Faker.Lorem.Sentence();

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(null);

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> Simple object equivalence <<-

    [Test]
    public async ValueTask equivalent_objects_with_same_properties_pass() {
        var name = Faker.Name.FirstName();
        var age  = Faker.Random.Int(1, 120);

        var actual   = new SimpleRecord(name, age);
        var expected = new SimpleRecord(name, age);

        await Assert.That(actual).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask objects_with_different_property_values_fail() {
        var age    = Faker.Random.Int(1, 120);
        var actual   = new SimpleRecord(Faker.Name.FirstName(), age);
        var expected = new SimpleRecord($"{Faker.Name.LastName()}_other", age);

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> Nested object equivalence <<-

    [Test]
    public async ValueTask equivalent_nested_objects_pass() {
        var label = Faker.Lorem.Word();
        var name  = Faker.Name.FirstName();
        var age   = Faker.Random.Int(1, 120);

        var actual   = new ParentRecord(label, new SimpleRecord(name, age));
        var expected = new ParentRecord(label, new SimpleRecord(name, age));

        await Assert.That(actual).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask nested_objects_with_different_inner_values_fail() {
        var label = Faker.Lorem.Word();
        var name  = Faker.Name.FirstName();

        var actual   = new ParentRecord(label, new SimpleRecord(name, Faker.Random.Int(1, 50)));
        var expected = new ParentRecord(label, new SimpleRecord(name, Faker.Random.Int(51, 100)));

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> Collection equivalence <<-

    [Test]
    public async ValueTask equivalent_lists_pass() {
        var items = Faker.Make(3, () => Faker.Random.Int());

        await Assert.That(items).IsDeepEquivalentTo(items.ToList());
    }

    [Test]
    public async ValueTask lists_with_different_elements_fail() {
        var actual   = Faker.Make(3, () => Faker.Random.Int(1, 100));
        var expected = actual.Select(x => x + 1000).ToList();

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask lists_with_different_counts_fail() {
        var actual   = Faker.Make(2, () => Faker.Random.Int());
        var expected = Faker.Make(3, () => Faker.Random.Int());

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask equivalent_arrays_pass() {
        var items = Faker.Make(3, () => Faker.Lorem.Word()).ToArray();

        await Assert.That(items).IsDeepEquivalentTo(items.ToArray());
    }

    [Test]
    public async ValueTask objects_containing_collections_pass() {
        var label = Faker.Lorem.Word();
        var items = Faker.Make(3, () => Faker.Random.Int()).ToList();

        var actual   = new RecordWithList(label, items);
        var expected = new RecordWithList(label, items.ToList());

        await Assert.That(actual).IsDeepEquivalentTo(expected);
    }

    #endregion

    #region ->> Type mismatch <<-

    [Test]
    public async ValueTask different_types_fail() {
        var value = Faker.Random.Int();

        var action = async () => await Assert.That(value).IsDeepEquivalentTo(value.ToString());

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> EquivalencyConfiguration: StringComparison <<-

    [Test]
    public async ValueTask case_insensitive_string_comparison_passes() {
        var word = Faker.Lorem.Word();

        await Assert.That(word.ToUpperInvariant()).IsDeepEquivalentTo(
            word.ToLowerInvariant(),
            cfg => cfg.WithStringComparison(StringComparison.OrdinalIgnoreCase)
        );
    }

    [Test]
    public async ValueTask case_sensitive_string_comparison_fails_on_case_difference() {
        var word = Faker.Lorem.Word();

        var action = async () => await Assert.That(word.ToUpperInvariant()).IsDeepEquivalentTo(
            word.ToLowerInvariant(),
            cfg => cfg.WithStringComparison(StringComparison.Ordinal)
        );

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> EquivalencyConfiguration: NumericTolerance <<-

    [Test]
    public async ValueTask numeric_tolerance_passes_within_range() {
        var value     = Faker.Random.Double(1, 1000);
        var tolerance = 0.1;
        var nearby    = value + Faker.Random.Double(0, tolerance);

        await Assert.That(value).IsDeepEquivalentTo(
            nearby,
            cfg => cfg.WithNumericTolerance(tolerance)
        );
    }

    [Test]
    public async ValueTask numeric_tolerance_fails_outside_range() {
        var value     = Faker.Random.Double(1, 1000);
        var tolerance = 0.1;
        var farAway   = value + tolerance + 1.0;

        var action = async () => await Assert.That(value).IsDeepEquivalentTo(
            farAway,
            cfg => cfg.WithNumericTolerance(tolerance)
        );

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> EquivalencyConfiguration: IgnoreCollectionOrder <<-

    [Test]
    public async ValueTask ignore_collection_order_passes_for_reordered_lists() {
        var items    = Faker.Make(5, () => Faker.Random.Int());
        var shuffled = items.OrderBy(_ => Faker.Random.Int()).ToList();

        await Assert.That(items).IsDeepEquivalentTo(
            shuffled,
            cfg => cfg.IgnoringCollectionOrder()
        );
    }

    [Test]
    public async ValueTask ordered_collection_comparison_fails_for_reordered_lists() {
        var items    = Enumerable.Range(0, 3).Select(_ => Faker.Random.Int()).ToList();
        var reversed = items.AsEnumerable().Reverse().ToList();

        var action = async () => await Assert.That(items).IsDeepEquivalentTo(reversed);

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> EquivalencyConfiguration: Excluding <<-

    [Test]
    public async ValueTask excluding_property_by_expression_ignores_difference() {
        var name = Faker.Name.FirstName();

        var actual   = new SimpleRecord(name, Faker.Random.Int(1, 50));
        var expected = new SimpleRecord(name, Faker.Random.Int(51, 100));

        await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.Excluding<SimpleRecord>(x => x.Age)
        );
    }

    [Test]
    public async ValueTask excluding_property_by_path_ignores_difference() {
        var name = Faker.Name.FirstName();

        var actual   = new SimpleRecord(name, Faker.Random.Int(1, 50));
        var expected = new SimpleRecord(name, Faker.Random.Int(51, 100));

        await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.Excluding("Age")
        );
    }

    #endregion

    #region ->> EquivalencyConfiguration: Custom comparer <<-

    [Test]
    public async ValueTask custom_comparer_is_used_for_type() {
        var word = Faker.Lorem.Word();
        var age  = Faker.Random.Int(1, 120);

        var actual   = new SimpleRecord(word.ToUpperInvariant(), age);
        var expected = new SimpleRecord(word.ToLowerInvariant(), age);

        await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.Using<string>((a, b) => a.Equals(b, StringComparison.OrdinalIgnoreCase))
        );
    }

    [Test]
    public async ValueTask custom_comparer_fails_when_comparison_returns_false() {
        var age = Faker.Random.Int(1, 120);

        var actual   = new SimpleRecord(Faker.Name.FirstName(), age);
        var expected = new SimpleRecord(Faker.Name.LastName() + "_other", age);

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.Using<string>((a, b) => a.Equals(b, StringComparison.OrdinalIgnoreCase))
        );

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> EquivalencyConfiguration: String normalization <<-

    [Test]
    public async ValueTask ignoring_newline_style_treats_crlf_and_lf_as_equal() {
        var text = Faker.Lorem.Paragraph();
        var withLf   = text.Replace("\r\n", "\n");
        var withCrLf = text.Replace("\n", "\r\n");

        await Assert.That(withLf).IsDeepEquivalentTo(
            withCrLf,
            cfg => cfg.IgnoringNewlineStyle()
        );
    }

    [Test]
    public async ValueTask ignoring_newline_style_still_detects_text_differences() {
        var actual   = "line1\nline2";
        var expected = "line1\nline3";

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.IgnoringNewlineStyle()
        );

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask ignoring_leading_whitespace_passes() {
        var word = Faker.Lorem.Word();

        await Assert.That($"  {word}").IsDeepEquivalentTo(
            word,
            cfg => cfg.IgnoringLeadingWhitespace()
        );
    }

    [Test]
    public async ValueTask ignoring_trailing_whitespace_passes() {
        var word = Faker.Lorem.Word();

        await Assert.That($"{word}  ").IsDeepEquivalentTo(
            word,
            cfg => cfg.IgnoringTrailingWhitespace()
        );
    }

    [Test]
    public async ValueTask whitespace_normalization_combines_with_case_insensitive() {
        var word = Faker.Lorem.Word();

        await Assert.That($"  {word.ToUpper()}  ").IsDeepEquivalentTo(
            word.ToLower(),
            cfg => cfg
                .IgnoringLeadingWhitespace()
                .IgnoringTrailingWhitespace()
                .WithStringComparison(StringComparison.OrdinalIgnoreCase)
        );
    }

    #endregion

    #region ->> EquivalencyConfiguration: ComparingEnumsByName <<-

    [Test]
    public async ValueTask comparing_enums_by_name_passes_for_matching_names() {
        var original = ComplexObjectFaker.Generate();
        var clone    = CloneComplexObject(original);

        await Assert.That(original).IsDeepEquivalentTo(
            clone,
            cfg => cfg.ComparingEnumsByName()
        );
    }

    [Test]
    public async ValueTask comparing_enums_by_name_fails_for_different_names() {
        var original = ComplexObjectFaker.Generate();
        var clone    = CloneComplexObject(original);
        clone.Priority = original.Priority == Priority.Critical ? Priority.Low : Priority.Critical;

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(
            clone,
            cfg => cfg.ComparingEnumsByName()
        );

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> EquivalencyConfiguration: Recursion control <<-

    [Test]
    public async ValueTask excluding_nested_objects_compares_only_top_level_members() {
        var actual = new Level {
            Name  = Faker.Lorem.Word(),
            Child = new Level { Name = Faker.Lorem.Word() }
        };

        var expected = new Level {
            Name  = actual.Name,
            Child = new Level { Name = $"{Faker.Lorem.Word()}_different" }
        };

        // Child is a nested reference type — should be skipped
        await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.ExcludingNestedObjects()
        );
    }

    [Test]
    public async ValueTask excluding_nested_objects_still_compares_value_types_and_strings() {
        var actual = new Level {
            Name  = Faker.Lorem.Word(),
            Child = new Level { Name = Faker.Lorem.Word() }
        };

        var expected = new Level {
            Name  = $"{Faker.Lorem.Word()}_different",
            Child = new Level { Name = Faker.Lorem.Word() }
        };

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.ExcludingNestedObjects()
        );

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask max_recursion_depth_throws_when_exceeded() {
        var deep = new Level { Name = "L0" };
        var current = deep;
        for (var i = 1; i <= 5; i++) {
            current.Child = new Level { Name = $"L{i}" };
            current = current.Child;
        }

        var clone = new Level { Name = "L0" };
        var cloneCurrent = clone;
        for (var i = 1; i <= 5; i++) {
            cloneCurrent.Child = new Level { Name = $"L{i}" };
            cloneCurrent = cloneCurrent.Child;
        }

        var action = async () => await Assert.That(deep).IsDeepEquivalentTo(
            clone,
            cfg => cfg.WithMaxRecursionDepth(2)
        );

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask max_recursion_depth_passes_when_within_limit() {
        var actual = new Level {
            Name  = "L0",
            Child = new Level { Name = "L1" }
        };

        var expected = new Level {
            Name  = "L0",
            Child = new Level { Name = "L1" }
        };

        await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.WithMaxRecursionDepth(5)
        );
    }

    #endregion

    #region ->> Circular reference handling <<-

    [Test]
    public async ValueTask circular_references_do_not_stack_overflow() {
        var name = Faker.Lorem.Word();

        var actual = new CircularNode(name);
        actual.Next = actual;

        var expected = new CircularNode(name);
        expected.Next = expected;

        await Assert.That(actual).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async ValueTask distinct_instances_with_structural_equality_are_compared_independently() {
        // Bug B: previousComparisons uses default Equals for key lookup.
        // If a type overrides Equals to be structural, two distinct instances
        // with the same key fields but different data fields get short-circuited
        // as "already compared", masking real differences.
        var tag       = Faker.Lorem.Word();
        var actualData   = Faker.Lorem.Sentence();
        var expectedData = $"{Faker.Lorem.Sentence()}_different";

        var actual = new NodeContainer {
            First  = new ValueEqualNode { Tag = tag, Data = actualData },
            Second = new ValueEqualNode { Tag = tag, Data = actualData }
        };

        var expected = new NodeContainer {
            First  = new ValueEqualNode { Tag = tag, Data = actualData },
            Second = new ValueEqualNode { Tag = tag, Data = expectedData }
        };

        // Second.Data differs — this MUST fail.
        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> EquivalencyConfiguration: Value type semantics <<-

    [Test]
    public async ValueTask comparing_by_value_uses_equals_instead_of_members() {
        var actual = new MoneyHolder {
            Name  = Faker.Commerce.ProductName(),
            Price = new Money { Amount = 10.00m, Currency = "USD" }
        };

        var expected = new MoneyHolder {
            Name  = actual.Name,
            Price = new Money { Amount = 10.00m, Currency = "USD" }
        };

        await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.ComparingByValue<Money>()
        );
    }

    [Test]
    public async ValueTask comparing_by_value_fails_when_equals_returns_false() {
        var actual = new MoneyHolder {
            Name  = Faker.Commerce.ProductName(),
            Price = new Money { Amount = 10.00m, Currency = "USD" }
        };

        var expected = new MoneyHolder {
            Name  = actual.Name,
            Price = new Money { Amount = 20.00m, Currency = "USD" }
        };

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.ComparingByValue<Money>()
        );

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask comparing_by_members_forces_recursion_even_when_equals_overridden() {
        var tag = Faker.Lorem.Word();

        var actual = new NodeContainer {
            First  = new ValueEqualNode { Tag = tag, Data = Faker.Lorem.Sentence() },
            Second = new ValueEqualNode { Tag = "other", Data = Faker.Lorem.Sentence() }
        };

        var expected = new NodeContainer {
            First  = new ValueEqualNode { Tag = tag, Data = $"{Faker.Lorem.Sentence()}_different" },
            Second = new ValueEqualNode { Tag = "other", Data = actual.Second.Data }
        };

        // Default behavior already recurses by members. This test proves the explicit opt-in works.
        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.ComparingByMembers<ValueEqualNode>()
        );

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask custom_comparer_takes_precedence_over_comparing_by_value() {
        var actual = new MoneyHolder {
            Name  = Faker.Commerce.ProductName(),
            Price = new Money { Amount = 10.00m, Currency = "USD" }
        };

        var expected = new MoneyHolder {
            Name  = actual.Name,
            Price = new Money { Amount = 10.00m, Currency = "EUR" }
        };

        // Custom comparer ignores Currency; ComparingByValue uses .Equals() which checks Currency.
        // Using<T> takes precedence.
        await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg
                .ComparingByValue<Money>()
                .Using<Money>((a, b) => a.Amount == b.Amount)
        );
    }

    #endregion

    #region ->> Complex scenario: nested objects with config <<-

    [Test]
    public async ValueTask complex_nested_comparison_with_config_passes() {
        var label     = Faker.Lorem.Word();
        var name      = Faker.Name.FirstName();
        var age       = Faker.Random.Int(10, 100);
        var tolerance = 1.0;

        var actual   = new ParentRecord(label, new SimpleRecord(name, age + 1));
        var expected = new ParentRecord(label, new SimpleRecord(name, age));

        await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.WithNumericTolerance(tolerance)
        );
    }

    #endregion

    #region ->> EquivalencyConfiguration: ExcludingMembersNamed <<-

    [Test]
    public async ValueTask excluding_members_named_skips_at_all_depths() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.PrimaryAddress.City = $"{Faker.Address.City()}_different";
        modified.Addresses[0].City   = $"{Faker.Address.City()}_different";

        await Assert.That(original).IsDeepEquivalentTo(
            modified,
            cfg => cfg.ExcludingMembersNamed("City")
        );
    }

    [Test]
    public async ValueTask excluding_members_named_multiple_names() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Name  = $"{Faker.Name.FullName()}_different";
        modified.Email = $"{Faker.Internet.Email()}_different";

        await Assert.That(original).IsDeepEquivalentTo(
            modified,
            cfg => cfg.ExcludingMembersNamed("Name", "Email")
        );
    }

    [Test]
    public async ValueTask excluding_members_named_does_not_skip_other_members() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Age = original.Age + 10;

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(
            modified,
            cfg => cfg.ExcludingMembersNamed("Name")
        );

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> EquivalencyConfiguration: Excluding by type <<-

    [Test]
    public async ValueTask excluding_by_type_skips_all_members_of_that_type() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.CreatedAt = original.CreatedAt.AddYears(1);

        // Exclude all DateTime members
        await Assert.That(original).IsDeepEquivalentTo(
            modified,
            cfg => cfg.ExcludingType<DateTime>()
        );
    }

    [Test]
    public async ValueTask excluding_by_type_does_not_skip_other_types() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Name = $"{Faker.Name.FullName()}_different";

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(
            modified,
            cfg => cfg.ExcludingType<DateTime>()
        );

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> EquivalencyConfiguration: Including (whitelist mode) <<-

    [Test]
    public async ValueTask including_by_expression_compares_only_specified_members() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Age     = original.Age + 10;
        modified.Score   = original.Score + 100.0;
        modified.Balance = original.Balance + 999m;

        // Only Name and Email are compared; Age, Score, Balance differences are ignored
        await Assert.That(original).IsDeepEquivalentTo(
            modified,
            cfg => cfg.Including<ComplexObject>(x => x.Name, x => x.Email)
        );
    }

    [Test]
    public async ValueTask including_by_expression_fails_when_included_member_differs() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Name = $"{Faker.Name.FullName()}_different";

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(
            modified,
            cfg => cfg.Including<ComplexObject>(x => x.Name, x => x.Email)
        );

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask including_by_predicate_compares_only_matching_members() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Age = original.Age + 10;

        // Only include string properties
        await Assert.That(original).IsDeepEquivalentTo(
            modified,
            cfg => cfg.Including(m => m is PropertyInfo p && p.PropertyType == typeof(string))
        );
    }

    [Test]
    public async ValueTask including_and_excluding_throws_invalid_operation() {
        var action = async () => await Assert.That(new SimpleRecord("a", 1)).IsDeepEquivalentTo(
            new SimpleRecord("a", 1),
            cfg => cfg
                .Including<SimpleRecord>(x => x.Name)
                .Excluding<SimpleRecord>(x => x.Age)
        );

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> EquivalencyConfiguration: ExcludingMissingMembers <<-

    [Test]
    public async ValueTask excluding_missing_members_compares_shared_members_only() {
        var name  = Faker.Name.FullName();
        var email = Faker.Internet.Email();

        var actual = new FullUser {
            Name    = name,
            Email   = email,
            Age     = Faker.Random.Int(18, 99),
            Address = Faker.Address.FullAddress()
        };

        var expected = new UserSummary {
            Name  = name,
            Email = email
        };

        // FullUser has Age and Address that UserSummary lacks — should be skipped
        await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.ExcludingMissingMembers()
        );
    }

    [Test]
    public async ValueTask excluding_missing_members_fails_on_shared_member_difference() {
        var actual = new FullUser {
            Name    = Faker.Name.FullName(),
            Email   = Faker.Internet.Email(),
            Age     = Faker.Random.Int(18, 99),
            Address = Faker.Address.FullAddress()
        };

        var expected = new UserSummary {
            Name  = actual.Name,
            Email = $"{Faker.Internet.Email()}_different"
        };

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.ExcludingMissingMembers()
        );

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask excluding_missing_members_with_same_type_is_noop() {
        var original = ComplexObjectFaker.Generate();
        var clone    = CloneComplexObject(original);

        await Assert.That(original).IsDeepEquivalentTo(
            clone,
            cfg => cfg.ExcludingMissingMembers()
        );
    }

    #endregion

    #region ->> EquivalencyConfiguration: Per-path ordering <<-

    [Test]
    public async ValueTask strict_ordering_globally_fails_for_reordered_collection() {
        var items    = Faker.Make(3, () => Faker.Random.Int()).ToList();
        var reversed = items.AsEnumerable().Reverse().ToList();

        var action = async () => await Assert.That(items).IsDeepEquivalentTo(
            reversed,
            cfg => cfg.WithStrictOrdering()
        );

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask without_strict_ordering_for_path_overrides_global_strict() {
        var name  = Faker.Lorem.Word();
        var items = Faker.Make(3, () => Faker.Random.Int()).ToList();

        var actual = new MultiCollection {
            Name     = name,
            Ordered  = items.ToList(),
            Flexible = items.ToList()
        };

        var expected = new MultiCollection {
            Name     = name,
            Ordered  = items.ToList(),
            Flexible = items.AsEnumerable().Reverse().ToList()
        };

        // Global strict ordering, but Flexible is exempted
        await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.WithStrictOrdering().WithoutStrictOrderingFor("Flexible")
        );
    }

    [Test]
    public async ValueTask with_strict_ordering_for_path_overrides_global_unordered() {
        var name  = Faker.Lorem.Word();
        var items = Faker.Make(3, () => Faker.Random.Int()).ToList();

        var actual = new MultiCollection {
            Name     = name,
            Ordered  = items.AsEnumerable().Reverse().ToList(),
            Flexible = items.AsEnumerable().Reverse().ToList()
        };

        var expected = new MultiCollection {
            Name     = name,
            Ordered  = items.ToList(),
            Flexible = items.ToList()
        };

        // Global unordered, but Ordered is strictly ordered — should fail
        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.IgnoringCollectionOrder().WithStrictOrderingFor("Ordered")
        );

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> Complex object with all property types <<-

    [Test]
    public async ValueTask equivalent_complex_objects_pass() {
        var original = ComplexObjectFaker.Generate();
        var clone    = CloneComplexObject(original);

        await Assert.That(original).IsDeepEquivalentTo(clone);
    }

    [Test]
    public async ValueTask complex_object_with_different_guid_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Id = Faker.Random.Guid();

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask complex_object_with_different_string_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Email = $"{Faker.Internet.Email()}_other";

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask complex_object_with_different_double_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Score = original.Score + 100.0;

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask complex_object_with_different_decimal_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Balance = original.Balance + 1000m;

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask complex_object_with_different_bool_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.IsActive = !original.IsActive;

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask complex_object_with_different_datetime_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.CreatedAt = original.CreatedAt.AddDays(1);

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask complex_object_with_different_enum_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Priority = (Priority)(((int)original.Priority + 1) % 4);

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask complex_object_with_different_nested_object_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.PrimaryAddress = AddressFaker.Generate();

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask complex_object_with_different_list_element_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Addresses[0] = AddressFaker.Generate();

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask complex_object_with_different_list_count_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Addresses = modified.Addresses.Take(1).ToList();

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask complex_object_excluding_property_passes() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Score = original.Score + 999.0;

        await Assert.That(original).IsDeepEquivalentTo(
            modified,
            cfg => cfg.Excluding<ComplexObject>(x => x.Score)
        );
    }

    [Test]
    public async ValueTask complex_object_with_reordered_addresses_passes_when_order_ignored() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Addresses.Reverse();

        await Assert.That(original).IsDeepEquivalentTo(
            modified,
            cfg => cfg.IgnoringCollectionOrder()
        );
    }

    [Test]
    public async ValueTask complex_object_with_nullable_both_null_passes() {
        var original      = ComplexObjectFaker.Generate();
        original.NickName = null;
        var clone = CloneComplexObject(original);

        await Assert.That(original).IsDeepEquivalentTo(clone);
    }

    [Test]
    public async ValueTask complex_object_nullable_null_vs_non_null_fails() {
        var original      = ComplexObjectFaker.Generate();
        original.NickName = null;
        var modified      = CloneComplexObject(original);
        modified.NickName = Faker.Internet.UserName();

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> Other cases with implicit conversions <<-

    [Test]
    public async ValueTask equivalent_objects_with_implicit_conversions_pass() {
        var revision = Revision.From(Faker.Random.Int(1, 100));

        await Assert.That(revision).IsDeepEquivalentTo(revision.Value);
    }

    [Test]
    public async ValueTask implicit_conversion_to_int_passes() {
        var revision = Revision.From(Faker.Random.Int(1, 100));
        var intValue = (int)revision.Value;

        await Assert.That(revision).IsDeepEquivalentTo(intValue);
        await Assert.That(intValue).IsDeepEquivalentTo(revision);
    }

    [Test]
    public async ValueTask implicit_conversion_to_short_passes() {
        var revision   = Revision.From(Faker.Random.Int(1, 100));
        var shortValue = (short)revision.Value;

        await Assert.That(revision).IsDeepEquivalentTo(shortValue);
        await Assert.That(shortValue).IsDeepEquivalentTo(revision);
    }

    [Test]
    public async ValueTask implicit_conversion_to_double_passes() {
        var revision    = Revision.From(Faker.Random.Int(1, 100));
        var doubleValue = (double)revision.Value;

        await Assert.That(revision).IsDeepEquivalentTo(doubleValue);
        await Assert.That(doubleValue).IsDeepEquivalentTo(revision);
    }

    [Test]
    public async ValueTask implicit_conversion_to_decimal_passes() {
        var revision     = Revision.From(Faker.Random.Int(1, 100));
        var decimalValue = (decimal)revision.Value;

        await Assert.That(revision).IsDeepEquivalentTo(decimalValue);
        await Assert.That(decimalValue).IsDeepEquivalentTo(revision);
    }

    [Test]
    public async ValueTask implicit_conversion_with_different_numeric_value_fails() {
        var revision       = Revision.From(Faker.Random.Int(1, 99));
        var differentValue = (int)revision.Value + 1;

        var action = async () => await Assert.That(revision).IsDeepEquivalentTo(differentValue);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask implicit_conversion_reverse_direction_with_different_value_fails() {
        var revision       = Revision.From(Faker.Random.Int(1, 99));
        var differentValue = (int)revision.Value + 1;

        var action = async () => await Assert.That(differentValue).IsDeepEquivalentTo(revision);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask cross_type_numeric_comparison_preserves_precision_for_large_values() {
        var actual   = long.MaxValue;
        var expected = long.MaxValue - 1;

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask cross_type_integer_comparison_is_lossless() {
        var value = long.MaxValue;

        await Assert.That(value).IsDeepEquivalentTo(value);
    }

    [Test]
    [SuppressMessage("Usage", "TUnitAssertions0005:Assert.That(...) should not be used with a constant value")]
    public async ValueTask different_integer_types_with_same_value_pass() {
        await Assert.That(42).IsDeepEquivalentTo(42L);
        await Assert.That(42L).IsDeepEquivalentTo(42);
        await Assert.That((short)42).IsDeepEquivalentTo(42L);
        await Assert.That(42L).IsDeepEquivalentTo((short)42);
    }

    [Test]
    [SuppressMessage("Usage", "TUnitAssertions0005:Assert.That(...) should not be used with a constant value")]
    public async ValueTask different_integer_types_with_different_values_fail() {
        var action = async () => await Assert.That(42).IsDeepEquivalentTo(43L);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask implicit_string_conversion_passes() {
        var label = Label.From(Faker.Lorem.Word());

        await Assert.That(label).IsDeepEquivalentTo(label.Value);
        await Assert.That(label.Value).IsDeepEquivalentTo(label);
    }

    [Test]
    public async ValueTask implicit_string_conversion_with_different_value_fails() {
        var label = Label.From(Faker.Lorem.Word());

        var action = async () => await Assert.That(label).IsDeepEquivalentTo($"{label.Value}_different");

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask implicit_string_conversion_reverse_with_different_value_fails() {
        var label = Label.From(Faker.Lorem.Word());

        var action = async () => await Assert.That($"{label.Value}_different").IsDeepEquivalentTo(label);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask implicit_guid_conversion_passes() {
        var entityId = EntityId.From(Faker.Random.Guid());

        await Assert.That(entityId).IsDeepEquivalentTo(entityId.Value);
        await Assert.That(entityId.Value).IsDeepEquivalentTo(entityId);
    }

    [Test]
    public async ValueTask implicit_guid_conversion_with_different_value_fails() {
        var entityId = EntityId.From(Faker.Random.Guid());

        var action = async () => await Assert.That(entityId).IsDeepEquivalentTo(Faker.Random.Guid());

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async ValueTask implicit_guid_conversion_reverse_with_different_value_fails() {
        var entityId = EntityId.From(Faker.Random.Guid());

        var action = async () => await Assert.That(Faker.Random.Guid()).IsDeepEquivalentTo(entityId);

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region ->> Helpers <<-

    static ComplexObject CloneComplexObject(ComplexObject source) {
        return new ComplexObject {
            Id             = source.Id,
            Name           = source.Name,
            Email          = source.Email,
            Age            = source.Age,
            Score          = source.Score,
            Balance        = source.Balance,
            IsActive       = source.IsActive,
            CreatedAt      = source.CreatedAt,
            Priority       = source.Priority,
            NickName       = source.NickName,
            PrimaryAddress = CloneAddress(source.PrimaryAddress),
            Addresses      = source.Addresses.Select(CloneAddress).ToList()
        };

        static AddressObject CloneAddress(AddressObject source) =>
            new() {
                Street    = source.Street,
                City      = source.City,
                ZipCode   = source.ZipCode,
                Latitude  = source.Latitude,
                Longitude = source.Longitude
            };
    }

    #endregion

    #region ->> Fakers <<-

    static readonly Faker<AddressObject> AddressFaker = new Faker<AddressObject>()
        .StrictMode(true)
        .RuleFor(a => a.Street, f => f.Address.StreetAddress())
        .RuleFor(a => a.City, f => f.Address.City())
        .RuleFor(a => a.ZipCode, f => f.Address.ZipCode())
        .RuleFor(a => a.Latitude, f => f.Address.Latitude())
        .RuleFor(a => a.Longitude, f => f.Address.Longitude());

    static readonly Faker<ComplexObject> ComplexObjectFaker = new Faker<ComplexObject>()
        .StrictMode(true)
        .RuleFor(x => x.Id, f => f.Random.Guid())
        .RuleFor(x => x.Name, f => f.Name.FullName())
        .RuleFor(x => x.Email, f => f.Internet.Email())
        .RuleFor(x => x.Age, f => f.Random.Int(18, 99))
        .RuleFor(x => x.Score, f => f.Random.Double(0, 100))
        .RuleFor(x => x.Balance, f => f.Finance.Amount(0, 10_000))
        .RuleFor(x => x.IsActive, f => f.Random.Bool())
        .RuleFor(x => x.CreatedAt, f => f.Date.Recent())
        .RuleFor(x => x.Priority, f => f.Random.Enum<Priority>())
        .RuleFor(x => x.NickName, f => f.Internet.UserName())
        .RuleFor(x => x.PrimaryAddress, _ => AddressFaker.Generate())
        .RuleFor(x => x.Addresses, _ => AddressFaker.Generate(3));

    #endregion

    #region ->> Test fixtures <<-

    record SimpleRecord(string Name, int Age);

    record ParentRecord(string Label, SimpleRecord Child);

    record RecordWithList(string Label, List<int> Items);

    enum Priority { Low, Medium, High, Critical }

    class AddressObject {
        public string Street    { get; set; } = null!;
        public string City      { get; set; } = null!;
        public string ZipCode   { get; set; } = null!;
        public double Latitude  { get; set; }
        public double Longitude { get; set; }
    }

    class ComplexObject {
        public Guid           Id             { get; set; }
        public string         Name           { get; set; } = null!;
        public string         Email          { get; set; } = null!;
        public int            Age            { get; set; }
        public double         Score          { get; set; }
        public decimal        Balance        { get; set; }
        public bool           IsActive       { get; set; }
        public DateTime       CreatedAt      { get; set; }
        public Priority       Priority       { get; set; }
        public string?        NickName       { get; set; }
        public AddressObject  PrimaryAddress { get; set; } = null!;
        public List<AddressObject> Addresses { get; set; } = [];
    }

    class CircularNode(string name) {
        public string        Name { get; } = name;
        public CircularNode? Next { get; set; }
    }

    class ValueEqualNode {
        public string Tag  { get; set; } = null!;
        public string Data { get; set; } = null!;

        public override bool Equals(object? obj) => obj is ValueEqualNode other && Tag == other.Tag;
        public override int GetHashCode() => Tag.GetHashCode();
    }

    class NodeContainer {
        public ValueEqualNode First  { get; set; } = null!;
        public ValueEqualNode Second { get; set; } = null!;
    }

    class EnumContainer {
        public Priority Priority { get; set; }
    }

    class Level {
        public string Name  { get; set; } = null!;
        public Level? Child { get; set; }
    }

    class Money {
        public decimal Amount   { get; set; }
        public string  Currency { get; set; } = null!;

        public override bool Equals(object? obj) =>
            obj is Money other && Amount == other.Amount && Currency == other.Currency;

        public override int GetHashCode() => HashCode.Combine(Amount, Currency);
    }

    class MoneyHolder {
        public string Name  { get; set; } = null!;
        public Money  Price { get; set; } = null!;
    }

    class FullUser {
        public string Name    { get; set; } = null!;
        public string Email   { get; set; } = null!;
        public int    Age     { get; set; }
        public string Address { get; set; } = null!;
    }

    class UserSummary {
        public string Name  { get; set; } = null!;
        public string Email { get; set; } = null!;
    }

    class MultiCollection {
        public string      Name     { get; set; } = null!;
        public List<int>   Ordered  { get; set; } = [];
        public List<int>   Flexible { get; set; } = [];
    }

    public record Revision : IComparable<Revision>, IComparable {
        /// <summary>
        /// The end of a stream. Use this when reading a stream backwards or subscribing live to a stream.
        /// </summary>
        public static readonly Revision Max = new(long.MaxValue);

        /// <summary>
        /// The beginning (i.e., the first event) of a stream.
        /// </summary>
        public static readonly Revision Min = new((long)0);

        /// <summary>
        /// A special value that refers to an invalid, unassigned or default revision.
        /// </summary>
        public static readonly Revision Unset = new((long)-1000);

        Revision(long value) => Value = value;

        public long Value { get; private init; }

        public bool IsUnset => this == Unset;

        public static Revision From(long value) =>
            value switch {
                -1000 => Unset,
                >= 0  => new Revision(value),
                _     => throw new InvalidCastException($"Invalid revision value: {value}")
            };

        public static implicit operator long(Revision revision) => revision.Value;

        public static implicit operator Revision(long revision) => From(revision);

        #region ->> relational members <<-

        public int CompareTo(Revision? other) {
            if (ReferenceEquals(this, other))
                return 0;

            return other is null ? 1 : Value.CompareTo(other.Value);
        }

        public int CompareTo(object? obj) {
            if (obj is null)
                return 1;

            if (ReferenceEquals(this, obj))
                return 0;

            return obj is Revision other
                ? CompareTo(other)
                : throw new ArgumentException($"Object must be of type {nameof(Revision)}");
        }

        public static bool operator <(Revision? left, Revision? right) =>
            Comparer<Revision>.Default.Compare(left, right) < 0;

        public static bool operator >(Revision? left, Revision? right) =>
            Comparer<Revision>.Default.Compare(left, right) > 0;

        public static bool operator <=(Revision? left, Revision? right) =>
            Comparer<Revision>.Default.Compare(left, right) <= 0;

        public static bool operator >=(Revision? left, Revision? right) =>
            Comparer<Revision>.Default.Compare(left, right) >= 0;

        #endregion
    }

    record Label {
        Label(string value) => Value = value;

        public string Value { get; }

        public static Label From(string value) => new(value);

        public static implicit operator string(Label label) => label.Value;
        public static implicit operator Label(string value) => From(value);
    }

    record EntityId {
        EntityId(Guid value) => Value = value;

        public Guid Value { get; }

        public static EntityId From(Guid value) => new(value);

        public static implicit operator Guid(EntityId id) => id.Value;
        public static implicit operator EntityId(Guid value) => From(value);
    }

    #endregion
}
