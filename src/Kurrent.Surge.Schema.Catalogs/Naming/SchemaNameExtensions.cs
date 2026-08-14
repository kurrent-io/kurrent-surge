using System.Text.RegularExpressions;

namespace Kurrent.Surge.Schema.Naming;

static partial class SchemaNameExtensions {
	[GeneratedRegex(@"([\p{Lu}]+)([\p{Lu}][\p{Ll}])")]
	private static partial Regex AcronymBoundary();

	[GeneratedRegex(@"([\p{Ll}\d])([\p{Lu}])")]
	private static partial Regex CaseBoundary();

	[GeneratedRegex(@"[-\s]")]
	private static partial Regex Separators();

	extension(string input) {
        public string ToSnakeCase() =>
            Separators().Replace(CaseBoundary().Replace(AcronymBoundary().Replace(input, "$1_$2"), "$1_$2"), "_").ToLowerInvariant();

        public string ToKebabCase() => input.ToSnakeCase().Replace('_', '-');
    }
}
