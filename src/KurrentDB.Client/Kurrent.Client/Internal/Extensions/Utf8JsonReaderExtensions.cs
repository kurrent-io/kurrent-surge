using System.Globalization;
using System.Text.Json;

namespace Kurrent.Client;

static class Utf8JsonReaderExtensions {
    extension(ref Utf8JsonReader source) {
        public bool TryGetTimeSpan(out TimeSpan value) {
            if (source.TokenType == JsonTokenType.String && TimeSpan.TryParse(source.GetString(), CultureInfo.InvariantCulture, out value))
                return true;

            value = TimeSpan.Zero;
            return false;
        }
    }
}
