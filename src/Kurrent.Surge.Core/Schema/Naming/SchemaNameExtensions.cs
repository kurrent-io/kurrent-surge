using System.Text.RegularExpressions;

namespace Kurrent.Surge.Schema.Naming;

static partial class SchemaNameExtensions {
	[GeneratedRegex(@"([\p{Lu}]+)([\p{Lu}][\p{Ll}])")]
	private static partial Regex AcronymBoundary();

	[GeneratedRegex(@"([\p{Ll}\d])([\p{Lu}])")]
	private static partial Regex CaseBoundary();

	[GeneratedRegex(@"[-\s]")]
	private static partial Regex Separators();

	[GeneratedRegex(@"[-_\s]")]
	private static partial Regex WordSeparators();

	extension(string input) {
        public string ToSnakeCase() =>
            Separators().Replace(CaseBoundary().Replace(AcronymBoundary().Replace(input, "$1_$2"), "$1_$2"), "_").ToLowerInvariant();

        public string ToKebabCase() => input.ToSnakeCase().Replace('_', '-');

        /// <summary>
        /// Splits on <c>-</c>, <c>_</c>, whitespace and case changes, capitalises the first letter of
        /// each word, and joins the words with single spaces.
        /// </summary>
        public string ToTitleCase() {
            var words = WordSeparators()
                .Replace(CaseBoundary().Replace(AcronymBoundary().Replace(input, "$1 $2"), "$1 $2"), " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return string.Join(' ', words.Select(static word => $"{char.ToUpperInvariant(word[0])}{word[1..]}"));
        }
    }
}
