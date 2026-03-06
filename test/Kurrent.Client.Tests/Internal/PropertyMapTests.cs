namespace Kurrent.Client.Tests.Internal;

public class PropertyMapTests {
    record Target(string Name, int Age, Dictionary<string, object> Tags);

    #region . Registration and GetValue .

    [Test]
    public async Task resolves_single_alias() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name")
        );

        var result = table.GetValue("name", new Target("Alice", 30, new()));

        await Assert.That(result).IsEqualTo("Alice");
    }

    [Test]
    public async Task resolves_multiple_aliases_to_same_accessor() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name", "full_name", "display_name")
        );

        var target = new Target("Bob", 25, new());

        await Assert.That(table.GetValue("name", target)).IsEqualTo("Bob");
        await Assert.That(table.GetValue("full_name", target)).IsEqualTo("Bob");
        await Assert.That(table.GetValue("display_name", target)).IsEqualTo("Bob");
    }

    [Test]
    public async Task resolves_case_insensitively() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name")
        );

        var target = new Target("Carol", 40, new());

        await Assert.That(table.GetValue("NAME", target)).IsEqualTo("Carol");
        await Assert.That(table.GetValue("Name", target)).IsEqualTo("Carol");
        await Assert.That(table.GetValue("nAmE", target)).IsEqualTo("Carol");
    }

    [Test]
    public async Task returns_null_for_unrecognized_name() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name")
        );

        var result = table.GetValue("unknown", new Target("Dave", 50, new()));

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task resolves_multiple_independent_accessors() {
        var table = PropertyMap<Target>.Build(map => {
            map.Register(t => t.Name, "name");
            map.Register(t => t.Age, "age", "years");
        });

        var target = new Target("Eve", 35, new());

        await Assert.That(table.GetValue("name", target)).IsEqualTo("Eve");
        await Assert.That(table.GetValue("age", target)).IsEqualTo(35);
        await Assert.That(table.GetValue("years", target)).IsEqualTo(35);
    }

    #endregion

    #region . Duplicate Detection .

    [Test]
    public void throws_on_duplicate_alias_within_same_register_call() {
        Assert.Throws<ArgumentException>(() => PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name", "name")
        ));
    }

    [Test]
    public void throws_on_duplicate_alias_across_register_calls() {
        Assert.Throws<ArgumentException>(() => PropertyMap<Target>.Build(map => {
            map.Register(t => t.Name, "name");
            map.Register(t => t.Age, "name");
        }));
    }

    [Test]
    public void throws_on_duplicate_alias_case_insensitive() {
        Assert.Throws<ArgumentException>(() => PropertyMap<Target>.Build(map => {
            map.Register(t => t.Name, "Name");
            map.Register(t => t.Age, "NAME");
        }));
    }

    #endregion

    #region . HasProperty .

    [Test]
    public async Task has_property_returns_true_for_registered_alias() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name", "display_name")
        );

        await Assert.That(table.HasProperty("name")).IsTrue();
        await Assert.That(table.HasProperty("display_name")).IsTrue();
    }

    [Test]
    public async Task has_property_returns_false_for_unregistered_name() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name")
        );

        await Assert.That(table.HasProperty("unknown")).IsFalse();
    }

    [Test]
    public async Task has_property_is_case_insensitive() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name")
        );

        await Assert.That(table.HasProperty("NAME")).IsTrue();
    }

    #endregion

    #region . Count .

    [Test]
    public async Task count_reflects_total_alias_count() {
        var table = PropertyMap<Target>.Build(map => {
            map.Register(t => t.Name, "name", "full_name");
            map.Register(t => t.Age, "age");
        });

        await Assert.That(table.Count).IsEqualTo(3);
    }

    [Test]
    public async Task empty_table_has_zero_count() {
        var table = PropertyMap<Target>.Build(_ => { });

        await Assert.That(table.Count).IsEqualTo(0);
    }

    #endregion

    #region . Empty Table .

    [Test]
    public async Task empty_table_returns_null_for_any_name() {
        var table = PropertyMap<Target>.Build(_ => { });

        var result = table.GetValue("anything", new Target("X", 0, new()));

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task empty_table_has_property_returns_false() {
        var table = PropertyMap<Target>.Build(_ => { });

        await Assert.That(table.HasProperty("anything")).IsFalse();
    }

    #endregion

    #region . Builder Fluent API .

    [Test]
    public async Task register_returns_builder_for_chaining() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name")
               .Register(t => t.Age, "age")
        );

        var target = new Target("Frank", 45, new());

        await Assert.That(table.GetValue("name", target)).IsEqualTo("Frank");
        await Assert.That(table.GetValue("age", target)).IsEqualTo(45);
    }

    #endregion

    #region . Typed GetValue (Generic Specialization) .

    [Test]
    public async Task typed_resolve_returns_string_without_boxing() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name")
        );

        var result = table.GetValue<string>("name", new Target("Alice", 30, new()));

        await Assert.That(result).IsEqualTo("Alice");
    }

    [Test]
    public async Task typed_resolve_returns_value_type_without_boxing() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Age, "age")
        );

        var result = table.GetValue<int>("age", new Target("Bob", 42, new()));

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task typed_resolve_returns_default_for_unrecognized_name() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name")
        );

        var stringResult = table.GetValue<string>("unknown", new Target("X", 0, new()));
        var intResult    = table.GetValue<int>("unknown", new Target("X", 0, new()));

        await Assert.That(stringResult).IsNull();
        await Assert.That(intResult).IsEqualTo(0);
    }

    [Test]
    public async Task typed_resolve_falls_back_to_untyped_on_type_mismatch() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name")
        );

        // Name is string, requesting object — should still work via untyped path
        var result = table.GetValue<object>("name", new Target("Carol", 30, new()));

        await Assert.That(result).IsEqualTo("Carol");
    }

    [Test]
    public async Task typed_resolve_with_multiple_aliases() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Age, "age", "years", "how_old")
        );

        var target = new Target("Dave", 55, new());

        await Assert.That(table.GetValue<int>("age", target)).IsEqualTo(55);
        await Assert.That(table.GetValue<int>("years", target)).IsEqualTo(55);
        await Assert.That(table.GetValue<int>("how_old", target)).IsEqualTo(55);
    }

    [Test]
    public async Task untyped_resolve_still_works_with_typed_registration() {
        var table = PropertyMap<Target>.Build(map => {
            map.Register(t => t.Name, "name");
            map.Register(t => t.Age, "age");
        });

        var target = new Target("Eve", 35, new());

        // Untyped path returns object? (boxes int)
        await Assert.That(table.GetValue("name", target)).IsEqualTo("Eve");
        await Assert.That(table.GetValue("age", target)).IsEqualTo(35);
    }

    #endregion

    #region . Keyed GetValue .

    [Test]
    public async Task keyed_resolve_returns_value_for_key() {
        var table = PropertyMap<Target>.Build(map =>
            map.RegisterKeyed((t, key) => t.Tags.TryGetValue(key!, out var v) ? v : null, "tags", "metadata")
        );

        var target = new Target("Alice", 30, new() { ["region"] = "us-east" });

        await Assert.That(table.GetValue("tags", "region", target)).IsEqualTo("us-east");
        await Assert.That(table.GetValue("metadata", "region", target)).IsEqualTo("us-east");
    }

    [Test]
    public async Task keyed_resolve_returns_null_when_key_not_found() {
        var table = PropertyMap<Target>.Build(map =>
            map.RegisterKeyed((t, key) => t.Tags.TryGetValue(key!, out var v) ? v : null, "tags")
        );

        var target = new Target("Bob", 25, new() { ["region"] = "eu-west" });

        await Assert.That(table.GetValue("tags", "missing", target)).IsNull();
    }

    [Test]
    public async Task keyed_resolve_without_key_falls_back_to_scalar_path() {
        var table = PropertyMap<Target>.Build(map =>
            map.RegisterKeyed((t, key) => key is not null && t.Tags.TryGetValue(key, out var v) ? v : null, "tags")
        );

        var target = new Target("Carol", 40, new() { ["x"] = "y" });

        // null key → scalar Get path on keyed accessor
        await Assert.That(table.GetValue("tags", null, target)).IsNull();
    }

    [Test]
    public async Task keyed_resolve_on_scalar_accessor_ignores_key() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name")
        );

        var target = new Target("Dave", 50, new());

        // scalar accessor with key → key is ignored, returns scalar value
        await Assert.That(table.GetValue("name", "whatever", target)).IsEqualTo("Dave");
    }

    [Test]
    public async Task mixed_scalar_and_keyed_registration() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name")
               .Register(t => t.Age, "age")
               .RegisterKeyed((t, key) => t.Tags.TryGetValue(key!, out var v) ? v : null, "tags")
        );

        var target = new Target("Eve", 35, new() { ["env"] = "prod" });

        await Assert.That(table.GetValue("name", target)).IsEqualTo("Eve");
        await Assert.That(table.GetValue("age", target)).IsEqualTo(35);
        await Assert.That(table.GetValue("tags", "env", target)).IsEqualTo("prod");
    }

    [Test]
    public void keyed_register_throws_on_duplicate_alias() {
        Assert.Throws<ArgumentException>(() => PropertyMap<Target>.Build(map => {
            map.Register(t => t.Name, "name");
            map.RegisterKeyed((t, key) => t.Tags.TryGetValue(key!, out var v) ? v : null, "name");
        }));
    }

    [Test]
    public async Task keyed_register_returns_builder_for_chaining() {
        var table = PropertyMap<Target>.Build(map =>
            map.RegisterKeyed((t, key) => t.Tags.TryGetValue(key!, out var v) ? v : null, "tags")
               .Register(t => t.Name, "name")
        );

        await Assert.That(table.Count).IsEqualTo(2);
    }

    [Test]
    public async Task keyed_aliases_count_in_total() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name")
               .RegisterKeyed((t, key) => t.Tags.TryGetValue(key!, out var v) ? v : null, "tags", "metadata")
        );

        await Assert.That(table.Count).IsEqualTo(3);
    }

    #endregion

#if NET9_0_OR_GREATER

    #region . Span-Based GetValue .

    [Test]
    public async Task span_resolve_returns_same_result_as_string_resolve() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name", "display_name")
        );

        var target = new Target("Grace", 28, new());

        await Assert.That(table.GetValue("name".AsSpan(), target)).IsEqualTo("Grace");
        await Assert.That(table.GetValue("display_name".AsSpan(), target)).IsEqualTo("Grace");
    }

    [Test]
    public async Task span_resolve_returns_null_for_unrecognized_name() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name")
        );

        await Assert.That(table.GetValue("unknown".AsSpan(), new Target("H", 0, new()))).IsNull();
    }

    [Test]
    public async Task span_has_property_matches_string_has_property() {
        var table = PropertyMap<Target>.Build(map =>
            map.Register(t => t.Name, "name")
        );

        await Assert.That(table.HasProperty("name".AsSpan())).IsTrue();
        await Assert.That(table.HasProperty("unknown".AsSpan())).IsFalse();
    }

    #endregion

#endif
}
