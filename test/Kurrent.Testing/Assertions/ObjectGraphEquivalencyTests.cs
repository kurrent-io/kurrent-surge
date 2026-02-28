// ReSharper disable ConvertToLocalFunction
// ReSharper disable PropertyCanBeMadeInitOnly.Local
// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable NotAccessedPositionalProperty.Local

using System.Diagnostics.CodeAnalysis;
using Bogus;
using Kurrent.Testing.Bogus;

namespace Kurrent.Testing.Assertions;

[Category("Testing")]
public class ObjectGraphEquivalencyTests {
    static readonly BogusFaker Faker = BogusFaker.Shared;

    #region Primitive & value type equivalence

    [Test]
    public async Task equivalent_integers_pass() {
        var value = Faker.Random.Int();

        await Assert.That(value).IsDeepEquivalentTo(value);
    }

    [Test]
    public async Task different_integers_fail() {
        var actual   = Faker.Random.Int(1, 1000);
        var expected = actual + 1;

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task equivalent_strings_pass() {
        var value = Faker.Lorem.Word();

        await Assert.That(value).IsDeepEquivalentTo(value);
    }

    [Test]
    public async Task different_strings_fail() {
        var actual   = Faker.Lorem.Word();
        var expected = $"{Faker.Lorem.Word()}_different";

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task equivalent_booleans_pass() {
        var value = Faker.Random.Bool();

        await Assert.That(value).IsDeepEquivalentTo(value);
    }

    [Test]
    public async Task equivalent_guids_pass() {
        var guid = Faker.Random.Guid();

        await Assert.That(guid).IsDeepEquivalentTo(guid);
    }

    [Test]
    public async Task equivalent_datetimes_pass() {
        var dt = Faker.Date.Recent();

        await Assert.That(dt).IsDeepEquivalentTo(dt);
    }

    #endregion

    #region Null handling

    [Test]
    [SuppressMessage("Usage", "TUnitAssertions0005:Assert.That(...) should not be used with a constant value")]
    public async Task both_null_passes() {
        await Assert.That(null).IsDeepEquivalentTo(null);
    }

    [Test]
    [SuppressMessage("Usage", "TUnitAssertions0005:Assert.That(...) should not be used with a constant value")]
    public async Task actual_null_expected_non_null_fails() {
        var expected = Faker.Lorem.Sentence();

        var action = async () => await Assert.That(null).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task actual_non_null_expected_null_fails() {
        var actual = Faker.Lorem.Sentence();

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(null);

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region Simple object equivalence

    [Test]
    public async Task equivalent_objects_with_same_properties_pass() {
        var name = Faker.Name.FirstName();
        var age  = Faker.Random.Int(1, 120);

        var actual   = new SimpleRecord(name, age);
        var expected = new SimpleRecord(name, age);

        await Assert.That(actual).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async Task objects_with_different_property_values_fail() {
        var age    = Faker.Random.Int(1, 120);
        var actual   = new SimpleRecord(Faker.Name.FirstName(), age);
        var expected = new SimpleRecord($"{Faker.Name.LastName()}_other", age);

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region Nested object equivalence

    [Test]
    public async Task equivalent_nested_objects_pass() {
        var label = Faker.Lorem.Word();
        var name  = Faker.Name.FirstName();
        var age   = Faker.Random.Int(1, 120);

        var actual   = new ParentRecord(label, new SimpleRecord(name, age));
        var expected = new ParentRecord(label, new SimpleRecord(name, age));

        await Assert.That(actual).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async Task nested_objects_with_different_inner_values_fail() {
        var label = Faker.Lorem.Word();
        var name  = Faker.Name.FirstName();

        var actual   = new ParentRecord(label, new SimpleRecord(name, Faker.Random.Int(1, 50)));
        var expected = new ParentRecord(label, new SimpleRecord(name, Faker.Random.Int(51, 100)));

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region Collection equivalence

    [Test]
    public async Task equivalent_lists_pass() {
        var items = Faker.Make(3, () => Faker.Random.Int());

        await Assert.That(items).IsDeepEquivalentTo(items.ToList());
    }

    [Test]
    public async Task lists_with_different_elements_fail() {
        var actual   = Faker.Make(3, () => Faker.Random.Int(1, 100));
        var expected = actual.Select(x => x + 1000).ToList();

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task lists_with_different_counts_fail() {
        var actual   = Faker.Make(2, () => Faker.Random.Int());
        var expected = Faker.Make(3, () => Faker.Random.Int());

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(expected);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task equivalent_arrays_pass() {
        var items = Faker.Make(3, () => Faker.Lorem.Word()).ToArray();

        await Assert.That(items).IsDeepEquivalentTo(items.ToArray());
    }

    [Test]
    public async Task objects_containing_collections_pass() {
        var label = Faker.Lorem.Word();
        var items = Faker.Make(3, () => Faker.Random.Int()).ToList();

        var actual   = new RecordWithList(label, items);
        var expected = new RecordWithList(label, items.ToList());

        await Assert.That(actual).IsDeepEquivalentTo(expected);
    }

    #endregion

    #region Type mismatch

    [Test]
    public async Task different_types_fail() {
        var value = Faker.Random.Int();

        var action = async () => await Assert.That(value).IsDeepEquivalentTo(value.ToString());

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region EquivalencyConfiguration: StringComparison

    [Test]
    public async Task case_insensitive_string_comparison_passes() {
        var word = Faker.Lorem.Word();

        await Assert.That(word.ToUpperInvariant()).IsDeepEquivalentTo(
            word.ToLowerInvariant(),
            cfg => cfg.WithStringComparison(StringComparison.OrdinalIgnoreCase)
        );
    }

    [Test]
    public async Task case_sensitive_string_comparison_fails_on_case_difference() {
        var word = Faker.Lorem.Word();

        var action = async () => await Assert.That(word.ToUpperInvariant()).IsDeepEquivalentTo(
            word.ToLowerInvariant(),
            cfg => cfg.WithStringComparison(StringComparison.Ordinal)
        );

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region EquivalencyConfiguration: NumericTolerance

    [Test]
    public async Task numeric_tolerance_passes_within_range() {
        var value     = Faker.Random.Double(1, 1000);
        var tolerance = 0.1;
        var nearby    = value + Faker.Random.Double(0, tolerance);

        await Assert.That(value).IsDeepEquivalentTo(
            nearby,
            cfg => cfg.WithNumericTolerance(tolerance)
        );
    }

    [Test]
    public async Task numeric_tolerance_fails_outside_range() {
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

    #region EquivalencyConfiguration: IgnoreCollectionOrder

    [Test]
    public async Task ignore_collection_order_passes_for_reordered_lists() {
        var items    = Faker.Make(5, () => Faker.Random.Int());
        var shuffled = items.OrderBy(_ => Faker.Random.Int()).ToList();

        await Assert.That(items).IsDeepEquivalentTo(
            shuffled,
            cfg => cfg.IgnoringCollectionOrder()
        );
    }

    [Test]
    public async Task ordered_collection_comparison_fails_for_reordered_lists() {
        var items    = Enumerable.Range(0, 3).Select(_ => Faker.Random.Int()).ToList();
        var reversed = items.AsEnumerable().Reverse().ToList();

        var action = async () => await Assert.That(items).IsDeepEquivalentTo(reversed);

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region EquivalencyConfiguration: Excluding

    [Test]
    public async Task excluding_property_by_expression_ignores_difference() {
        var name = Faker.Name.FirstName();

        var actual   = new SimpleRecord(name, Faker.Random.Int(1, 50));
        var expected = new SimpleRecord(name, Faker.Random.Int(51, 100));

        await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.Excluding<SimpleRecord>(x => x.Age)
        );
    }

    [Test]
    public async Task excluding_property_by_path_ignores_difference() {
        var name = Faker.Name.FirstName();

        var actual   = new SimpleRecord(name, Faker.Random.Int(1, 50));
        var expected = new SimpleRecord(name, Faker.Random.Int(51, 100));

        await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.Excluding("Age")
        );
    }

    #endregion

    #region EquivalencyConfiguration: Custom comparer

    [Test]
    public async Task custom_comparer_is_used_for_type() {
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
    public async Task custom_comparer_fails_when_comparison_returns_false() {
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

    #region EquivalencyConfiguration: String normalization

    [Test]
    public async Task ignoring_newline_style_treats_crlf_and_lf_as_equal() {
        var text = Faker.Lorem.Paragraph();
        var withLf   = text.Replace("\r\n", "\n");
        var withCrLf = text.Replace("\n", "\r\n");

        await Assert.That(withLf).IsDeepEquivalentTo(
            withCrLf,
            cfg => cfg.IgnoringNewlineStyle()
        );
    }

    [Test]
    public async Task ignoring_newline_style_still_detects_text_differences() {
        var actual   = "line1\nline2";
        var expected = "line1\nline3";

        var action = async () => await Assert.That(actual).IsDeepEquivalentTo(
            expected,
            cfg => cfg.IgnoringNewlineStyle()
        );

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task ignoring_leading_whitespace_passes() {
        var word = Faker.Lorem.Word();

        await Assert.That($"  {word}").IsDeepEquivalentTo(
            word,
            cfg => cfg.IgnoringLeadingWhitespace()
        );
    }

    [Test]
    public async Task ignoring_trailing_whitespace_passes() {
        var word = Faker.Lorem.Word();

        await Assert.That($"{word}  ").IsDeepEquivalentTo(
            word,
            cfg => cfg.IgnoringTrailingWhitespace()
        );
    }

    [Test]
    public async Task whitespace_normalization_combines_with_case_insensitive() {
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

    #region EquivalencyConfiguration: ComparingEnumsByName

    [Test]
    public async Task comparing_enums_by_name_passes_for_matching_names() {
        var original = ComplexObjectFaker.Generate();
        var clone    = CloneComplexObject(original);

        await Assert.That(original).IsDeepEquivalentTo(
            clone,
            cfg => cfg.ComparingEnumsByName()
        );
    }

    [Test]
    public async Task comparing_enums_by_name_fails_for_different_names() {
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

    #region EquivalencyConfiguration: Recursion control

    [Test]
    public async Task excluding_nested_objects_compares_only_top_level_members() {
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
    public async Task excluding_nested_objects_still_compares_value_types_and_strings() {
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
    public async Task max_recursion_depth_throws_when_exceeded() {
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
    public async Task max_recursion_depth_passes_when_within_limit() {
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

    #region Circular reference handling

    [Test]
    public async Task circular_references_do_not_stack_overflow() {
        var name = Faker.Lorem.Word();

        var actual = new CircularNode(name);
        actual.Next = actual;

        var expected = new CircularNode(name);
        expected.Next = expected;

        await Assert.That(actual).IsDeepEquivalentTo(expected);
    }

    [Test]
    public async Task distinct_instances_with_structural_equality_are_compared_independently() {
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

    #region EquivalencyConfiguration: Value type semantics

    [Test]
    public async Task comparing_by_value_uses_equals_instead_of_members() {
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
    public async Task comparing_by_value_fails_when_equals_returns_false() {
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
    public async Task comparing_by_members_forces_recursion_even_when_equals_overridden() {
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
    public async Task custom_comparer_takes_precedence_over_comparing_by_value() {
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

    #region Complex scenario: nested objects with config

    [Test]
    public async Task complex_nested_comparison_with_config_passes() {
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

    #region EquivalencyConfiguration: ExcludingMembersNamed

    [Test]
    public async Task excluding_members_named_skips_at_all_depths() {
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
    public async Task excluding_members_named_multiple_names() {
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
    public async Task excluding_members_named_does_not_skip_other_members() {
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

    #region EquivalencyConfiguration: Excluding by type

    [Test]
    public async Task excluding_by_type_skips_all_members_of_that_type() {
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
    public async Task excluding_by_type_does_not_skip_other_types() {
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

    #region Complex object with all property types

    [Test]
    public async Task equivalent_complex_objects_pass() {
        var original = ComplexObjectFaker.Generate();
        var clone    = CloneComplexObject(original);

        await Assert.That(original).IsDeepEquivalentTo(clone);
    }

    [Test]
    public async Task complex_object_with_different_guid_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Id = Faker.Random.Guid();

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task complex_object_with_different_string_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Email = $"{Faker.Internet.Email()}_other";

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task complex_object_with_different_double_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Score = original.Score + 100.0;

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task complex_object_with_different_decimal_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Balance = original.Balance + 1000m;

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task complex_object_with_different_bool_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.IsActive = !original.IsActive;

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task complex_object_with_different_datetime_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.CreatedAt = original.CreatedAt.AddDays(1);

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task complex_object_with_different_enum_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Priority = (Priority)(((int)original.Priority + 1) % 4);

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task complex_object_with_different_nested_object_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.PrimaryAddress = AddressFaker.Generate();

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task complex_object_with_different_list_element_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Addresses[0] = AddressFaker.Generate();

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task complex_object_with_different_list_count_fails() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Addresses = modified.Addresses.Take(1).ToList();

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task complex_object_excluding_property_passes() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Score = original.Score + 999.0;

        await Assert.That(original).IsDeepEquivalentTo(
            modified,
            cfg => cfg.Excluding<ComplexObject>(x => x.Score)
        );
    }

    [Test]
    public async Task complex_object_with_reordered_addresses_passes_when_order_ignored() {
        var original = ComplexObjectFaker.Generate();
        var modified = CloneComplexObject(original);
        modified.Addresses.Reverse();

        await Assert.That(original).IsDeepEquivalentTo(
            modified,
            cfg => cfg.IgnoringCollectionOrder()
        );
    }

    [Test]
    public async Task complex_object_with_nullable_both_null_passes() {
        var original      = ComplexObjectFaker.Generate();
        original.NickName = null;
        var clone = CloneComplexObject(original);

        await Assert.That(original).IsDeepEquivalentTo(clone);
    }

    [Test]
    public async Task complex_object_nullable_null_vs_non_null_fails() {
        var original      = ComplexObjectFaker.Generate();
        original.NickName = null;
        var modified      = CloneComplexObject(original);
        modified.NickName = Faker.Internet.UserName();

        var action = async () => await Assert.That(original).IsDeepEquivalentTo(modified);

        await Assert.That(action).ThrowsException();
    }

    #endregion

    #region Helpers

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

    #region Fakers

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

    #region Test fixtures

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

    #endregion
}
