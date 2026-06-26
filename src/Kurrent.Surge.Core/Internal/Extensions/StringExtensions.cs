using System.Text;

namespace Kurrent.Client;

static class StringExtensions {
    extension(string source) {
        /// <summary>
        /// Converts a PascalCase, camelCase, kebab-case, or space-separated string to snake_case.
        /// Examples: <c>HelloWorld</c> → <c>hello_world</c>, <c>HTTPRequest</c> → <c>http_request</c>,
        /// <c>NotFound</c> → <c>not_found</c>, <c>my-value</c> → <c>my_value</c>.
        /// </summary>
        public string Underscore() {
            if (string.IsNullOrEmpty(source)) return source;

            var sb = new StringBuilder(source.Length + 8);

            for (var i = 0; i < source.Length; i++) {
                var c = source[i];

                if (c is '-' or ' ') {
                    sb.Append('_');
                    continue;
                }

                if (i > 0 && char.IsUpper(c)) {
                    var prev = source[i - 1];
                    var next = i + 1 < source.Length ? source[i + 1] : '\0';

                    // boundary between a lowercase/digit and an uppercase: camelCase → camel_Case
                    // boundary between an uppercase run and a capitalized word: HTTPRequest → HTTP_Request
                    if (char.IsLower(prev) || char.IsDigit(prev) ||
                        (char.IsUpper(prev) && char.IsLower(next)))
                        sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }
    }
}
