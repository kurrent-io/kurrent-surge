using System.Text.RegularExpressions;

namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>
/// <see cref="IModelLayout"/> for GGUF weights (llama.cpp / Ollama). A GGUF repo often holds one model at
/// several quantisations (<c>Q4_K_M</c>, <c>Q8_0</c>, …) and, for large models, a single quant split across
/// numbered shards (<c>model-00001-of-00003.gguf</c> …). Resolves the <c>.gguf</c> named by
/// <see cref="ModelRef.FilePath"/>, pulls in every shard of a split file, and includes a sibling
/// tokenizer/config when present.
/// </summary>
public sealed partial class GgufLayout : IModelLayout {
    /// <summary>Best-effort companions: GGUF is self-contained, so a missing tokenizer/config is not fatal.</summary>
    static readonly string[] OptionalCompanions = ["tokenizer.json", "config.json"];

    /// <inheritdoc />
    public ModelKind Kind => ModelKind.Gguf;

    /// <inheritdoc />
    public ModelPlan Select(IReadOnlyList<RemoteFile> files, ModelRef reference) {
        var gguf = files.Where(f => f.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)).ToList();

        if (gguf.Count == 0)
            throw new ModelException($"No '.gguf' files found in '{reference.Repo}'.");

        var chosen = gguf.FirstOrDefault(f => string.Equals(f.Path, reference.FilePath, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(chosen.Path))
            throw new ModelException($"Model file '{reference.FilePath}' was not found in '{reference.Repo}'.");

        var planned = ExpandShards(chosen, gguf, reference.Repo, out var primary);

        foreach (var name in OptionalCompanions) {
            var companion = files.FirstOrDefault(f => string.Equals(f.Path, name, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(companion.Path) &&
                !planned.Any(f => string.Equals(f.Path, companion.Path, StringComparison.OrdinalIgnoreCase)))
                planned.Add(companion);
        }

        return new(primary, planned);
    }

    // Canonical split naming: "<base>-00001-of-00003.gguf". base = shared prefix, index = this shard, total = count.
    [GeneratedRegex(@"^(?<base>.+)-(?<index>\d{5})-of-(?<total>\d{5})\.gguf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SplitPatternRegex();

    /// <summary>
    /// If <paramref name="chosen"/> is one shard of a split GGUF, returns every shard in the set (ordered by
    /// index), with <paramref name="primary"/> set to the first shard (<c>-00001-of-…</c>). Otherwise returns
    /// just the chosen file. Throws if a split set is incomplete.
    /// </summary>
    static List<RemoteFile> ExpandShards(RemoteFile chosen, List<RemoteFile> gguf, string repo, out RemoteFile primary) {
        var match = SplitPatternRegex().Match(FileName(chosen.Path));

        if (!match.Success) {
            primary = chosen;
            return [chosen];
        }

        var baseName = match.Groups["base"].Value;
        var total    = match.Groups["total"].Value;
        var dir      = DirOf(chosen.Path);

        var shards = gguf
            .Select(f => (File: f, Match: SplitPatternRegex().Match(FileName(f.Path))))
            .Where(x => x.Match.Success
                     && string.Equals(DirOf(x.File.Path), dir, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(x.Match.Groups["base"].Value, baseName, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(x.Match.Groups["total"].Value, total, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => int.Parse(x.Match.Groups["index"].Value))
            .Select(x => x.File)
            .ToList();

        var expected = int.Parse(total);

        if (shards.Count != expected)
            throw new ModelException($"Split GGUF '{baseName}-*-of-{total}.gguf' in '{repo}' is incomplete: found {shards.Count} of {expected} shards.");

        primary = shards[0];
        return shards;
    }

    static string FileName(string path) {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    static string DirOf(string path) {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[..slash] : "";
    }
}