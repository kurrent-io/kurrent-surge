using System.Text.RegularExpressions;

namespace Kurrent.Client.Schema.NameStrategies;

static class SchemaNameExtensions {
	extension(string input) {
        public string ToSnakeCase() =>
            Regex.Replace(Regex.Replace(Regex.Replace(input, @"([\p{Lu}]+)([\p{Lu}][\p{Ll}])", "$1_$2"), @"([\p{Ll}\d])([\p{Lu}])", "$1_$2"), @"[-\s]", "_").ToLowerInvariant();

        public string ToKebabCase() => input.ToSnakeCase().Replace('_', '-');
    }
}
