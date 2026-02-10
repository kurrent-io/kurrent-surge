using System.Buffers;

namespace Kurrent;

static class StringExtensions {
    /// <summary>
    /// Converts a string to lowercase and separates words with underscores, transforming
    /// PascalCase, camelCase, spaces, and dashes into underscore_case.
    /// </summary>
    /// <param name="input">The string to be underscored.</param>
    /// <returns>
    /// A lowercase string with words separated by underscores instead of spaces, case changes, or dashes.
    /// </returns>
    /// <example>
    /// <code>
    /// "SomePropertyName".Underscore() => "some_property_name"
    /// "somePropertyName".Underscore() => "some_property_name"
    /// "some-property-name".Underscore() => "some_property_name"
    /// "some property name".Underscore() => "some_property_name"
    /// "HTMLParser".Underscore()        => "html_parser"
    /// "IOStream".Underscore()          => "io_stream"
    /// </code>
    /// </example>
    public static string Underscore(this string input) {
        if (string.IsNullOrEmpty(input))
            return input;

        var maxLen = input.Length * 2;

        char[]? rented = null;

        var buffer = maxLen <= 256
            ? stackalloc char[256]
            : rented = ArrayPool<char>.Shared.Rent(maxLen);

        try {
            var pos = 0;

            for (var i = 0; i < input.Length; i++) {
                var c = input[i];

                if (c is '-' or ' ') {
                    buffer[pos++] = '_';
                    continue;
                }

                if (i > 0 && c is >= 'A' and <= 'Z') {
                    var prev = input[i - 1];

                    if (prev is >= 'a' and <= 'z' or >= '0' and <= '9')
                        // camelCase boundary: lowercase/digit then uppercase
                        buffer[pos++] = '_';
                    else if (prev is >= 'A' and <= 'Z'
                          && i + 1 < input.Length
                          && input[i + 1] is >= 'a' and <= 'z')
                        // acronym boundary: e.g., the P in "HTMLParser"
                        buffer[pos++] = '_';
                }

                buffer[pos++] = char.ToLowerInvariant(c);
            }

            return new string(buffer[..pos]);
        }
        finally {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }
}
