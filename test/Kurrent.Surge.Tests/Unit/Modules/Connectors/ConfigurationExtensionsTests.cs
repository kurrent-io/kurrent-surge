using Kurrent.Surge.Client.Connectors;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Surge.Tests.Connectors;

[Category("Connectors")]
[Category("Unit")]
public class ConfigurationExtensionsTests {
    #region ->> IConfiguration.ToFlatDictionary <<-

    [Test]
    public async ValueTask configuration_to_flat_dictionary_preserves_flat_keys() {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("foo", "1"),
                new("bar", "2"),
            ])
            .Build();

        var result = config.ToFlatDictionary();

        await Assert.That(result).IsEquivalentTo(new Dictionary<string, string> {
            ["foo"] = "1",
            ["bar"] = "2",
        });
    }

    [Test]
    public async ValueTask configuration_to_flat_dictionary_preserves_colon_separated_keys() {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("subscription:filter:scope", "stream"),
                new("subscription:initialPosition", "latest"),
            ])
            .Build();

        var result = config.ToFlatDictionary();

        await Assert.That(result["subscription:filter:scope"]).IsEqualTo("stream");
        await Assert.That(result["subscription:initialPosition"]).IsEqualTo("latest");
    }

    [Test]
    public async ValueTask configuration_section_to_flat_dictionary_strips_parent_path() {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("settings:foo", "1"),
                new("settings:bar:baz", "2"),
                new("other:noise", "ignored"),
            ])
            .Build();

        var result = config.GetSection("settings").ToFlatDictionary();

        await Assert.That(result["foo"]).IsEqualTo("1");
        await Assert.That(result["bar:baz"]).IsEqualTo("2");
        await Assert.That(result.ContainsKey("other:noise")).IsFalse();
        await Assert.That(result.ContainsKey("settings:foo")).IsFalse();
    }

    [Test]
    public async ValueTask configuration_to_flat_dictionary_filters_null_values() {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("present", "value"),
                new KeyValuePair<string, string?>("absent", null),
            ])
            .Build();

        var result = config.ToFlatDictionary();

        await Assert.That(result.ContainsKey("present")).IsTrue();
        await Assert.That(result.ContainsKey("absent")).IsFalse();
    }

    [Test]
    public async ValueTask empty_configuration_returns_empty_dictionary() {
        var config = new ConfigurationBuilder().Build();

        var result = config.ToFlatDictionary();

        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region ->> string.ToFlatDictionary (JSON) <<-

    [Test]
    public async ValueTask json_to_flat_dictionary_flattens_nested_objects_with_colons() {
        const string json = """
            {
                "subscription": {
                    "filter": { "scope": "stream", "expression": "test-" },
                    "initialPosition": "latest"
                }
            }
            """;

        var result = json.ToFlatDictionary();

        await Assert.That(result["subscription:filter:scope"]).IsEqualTo("stream");
        await Assert.That(result["subscription:filter:expression"]).IsEqualTo("test-");
        await Assert.That(result["subscription:initialPosition"]).IsEqualTo("latest");
    }

    [Test]
    public async ValueTask json_to_flat_dictionary_uses_numeric_indices_for_arrays() {
        const string json = """
            { "items": ["one", "two", "three"] }
            """;

        var result = json.ToFlatDictionary();

        await Assert.That(result["items:0"]).IsEqualTo("one");
        await Assert.That(result["items:1"]).IsEqualTo("two");
        await Assert.That(result["items:2"]).IsEqualTo("three");
    }

    [Test]
    public async ValueTask json_to_flat_dictionary_preserves_non_string_primitives_as_strings() {
        const string json = """
            { "count": 42, "enabled": true, "ratio": 0.5 }
            """;

        var result = json.ToFlatDictionary();

        await Assert.That(result.ContainsKey("count")).IsTrue();
        await Assert.That(result.ContainsKey("enabled")).IsTrue();
        await Assert.That(result.ContainsKey("ratio")).IsTrue();
        await Assert.That(result["count"]).IsEqualTo("42");
    }

    [Test]
    public async ValueTask json_to_flat_dictionary_excludes_null_leaves() {
        const string json = """
            { "set": "value", "unset": null }
            """;

        var result = json.ToFlatDictionary();

        await Assert.That(result.ContainsKey("set")).IsTrue();
        await Assert.That(result.ContainsKey("unset")).IsFalse();
    }

    [Test]
    public async ValueTask empty_json_object_returns_empty_dictionary() {
        var result = "{}".ToFlatDictionary();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async ValueTask invalid_json_throws() {
        await Assert.That(() => "not json".ToFlatDictionary()).ThrowsException();
    }

    #endregion
}
