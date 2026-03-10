namespace Kurrent.Variant.Tests.Result;

[Category("Variant")]
public class StringExtensionsTests {
    [Test]
    [Arguments("SomePropertyName", "some_property_name")]
    [Arguments("somePropertyName", "some_property_name")]
    [Arguments("some-property-name", "some_property_name")]
    [Arguments("some property name", "some_property_name")]
    public async ValueTask converts_common_formats_to_snake_case(string input, string expected) =>
        await Assert.That(input.Underscore()).IsEqualTo(expected);

    [Test]
    [Arguments("HTMLParser", "html_parser")]
    [Arguments("IOStream", "io_stream")]
    [Arguments("XMLHTTPRequest", "xmlhttp_request")]
    public async ValueTask splits_acronyms_at_word_boundaries(string input, string expected) =>
        await Assert.That(input.Underscore()).IsEqualTo(expected);

    [Test]
    [Arguments("order1Created", "order1_created")]
    [Arguments("get2ndItem", "get2nd_item")]
    public async ValueTask inserts_underscore_after_digit_followed_by_uppercase(string input, string expected) =>
        await Assert.That(input.Underscore()).IsEqualTo(expected);

    [Test]
    public async ValueTask returns_empty_string_unchanged() =>
        await Assert.That("".Underscore()).IsEqualTo("");

    [Test]
    public async ValueTask returns_single_character_lowercased() =>
        await Assert.That("A".Underscore()).IsEqualTo("a");

    [Test]
    public async ValueTask returns_already_snake_cased_string_unchanged() =>
        await Assert.That("already_snake_case".Underscore()).IsEqualTo("already_snake_case");
}
