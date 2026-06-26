using System.Globalization;
using System.Text.Json;

namespace Kurrent.Client;

static class Utf8JsonReaderExtensions {
    extension(ref Utf8JsonReader source) {
        public bool TryGetTimeSpan(out TimeSpan value) {
            // Require a ':' before parsing: TimeSpan.TryParse accepts bare integers as DAYS,
            // so without this guard a user string like "30" would morph into 30 days. Every
            // real TimeSpan serialized by the SDK uses the "c" format, which always has one.
            if (source.TokenType == JsonTokenType.String
             && source.GetString() is { } text
             && text.Contains(':')
             && TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value))
                return true;

            value = TimeSpan.Zero;
            return false;
        }
    }
}
