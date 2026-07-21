namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>
/// Parses an RFC 5988 <c>Link</c> header into a <c>rel → target URI</c> map. A header value is one or more
/// comma-separated links, each shaped <c>&lt;uri&gt;; rel="name"; …</c> — e.g. the Hugging Face pagination
/// header <c>&lt;https://huggingface.co/api/models/…?cursor=abc&gt;; rel="next"</c>.
/// </summary>
static class LinkHeaderParser {
    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> headerValues) {
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headerValues)
            foreach (var link in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                var open = link.IndexOf('<');
                if (open < 0)
                    continue;

                var close = link.IndexOf('>', open + 1);
                if (close < 0)
                    continue;

                var uri = link[(open + 1)..close].Trim();
                var rel = RelOf(link[(close + 1)..]);

                if (uri.Length > 0 && rel is not null)
                    links[rel] = uri; // last wins on a duplicate rel
            }

        return links;
    }

    // The rel value from a link's parameters — e.g. `; rel="next"; type="…"` → "next".
    static string? RelOf(string parameters) {
        foreach (var param in parameters.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            var eq = param.IndexOf('=');

            if (eq > 0 && string.Equals(param[..eq].Trim(), "rel", StringComparison.OrdinalIgnoreCase))
                return param[(eq + 1)..].Trim().Trim('"');
        }

        return null;
    }
}
