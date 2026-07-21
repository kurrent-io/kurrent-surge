namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>
/// <see cref="IModelLayout"/> for Safetensors weights. Resolves the single weight file named by
/// <see cref="ModelRef.FilePath"/>, or a sharded model when a <c>*.safetensors.index.json</c> is present —
/// every <c>.safetensors</c> shard plus the index — together with config + tokenizer companions.
/// </summary>
/// <remarks>
/// The sharded case discovers shards from the <em>listing</em> (every <c>.safetensors</c> file) rather than by
/// parsing the index's <c>weight_map</c>. Verified against real Hugging Face repos (Qwen2.5-7B: 4 shards,
/// Phi-3-mini: 2, zephyr-7b: 8) that the two sets are identical — a sharded repo contains exactly its shards
/// and the index — so the listing is authoritative and the layout stays pure (no download-then-parse step).
/// If a repo ever diverges, add an index-parse pass here.
/// </remarks>
public sealed class SafetensorsLayout : IModelLayout {
    const string IndexSuffix = ".safetensors.index.json";

    /// <summary>Config + tokenizer artifacts downloaded alongside the weights when present, in priority order.</summary>
    static readonly string[] Companions = ["config.json", "tokenizer.json", "tokenizer.model", "tokenizer_config.json", "vocab.json", "merges.txt", "special_tokens_map.json"];

    /// <inheritdoc />
    public ModelKind Kind => ModelKind.Safetensors;

    /// <inheritdoc />
    public ModelPlan Select(IReadOnlyList<RemoteFile> files, ModelRef reference) {
        var index = files.FirstOrDefault(f => f.Path.EndsWith(IndexSuffix, StringComparison.OrdinalIgnoreCase));

        RemoteFile primary;

        var planned = new List<RemoteFile>();

        if (!string.IsNullOrEmpty(index.Path)) {
            // Sharded: the index is the primary the loader opens; every .safetensors file is a shard.
            var shards = files
                .Where(f => f.Path.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Path, StringComparer.Ordinal)
                .ToList();

            if (shards.Count == 0)
                throw new ModelException($"'{index.Path}' is present in '{reference.Repo}' but no '.safetensors' shards were found.");

            primary = index;
            planned.Add(index);
            planned.AddRange(shards);
        } else {
            // Single weight file: the primary is the one named by the reference.
            var match = files.FirstOrDefault(f => string.Equals(f.Path, reference.FilePath, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(match.Path))
                throw new ModelException($"Model file '{reference.FilePath}' was not found in '{reference.Repo}'.");

            primary = match;
            planned.Add(primary);
        }

        // Rank companion candidates: same dir as the weights > repo root > shallowest > ordinal path.
        // Without this, a nested module sidecar (e.g. 1_Pooling/config.json on sentence-transformers repos)
        // that sorts before the root config.json in the tree listing would be picked instead — and since the
        // download flattens the bundle by file name, the bundle would ship the WRONG config.json contents.
        // Mirrors OnnxLayout's AddNamedArtifacts tie-break.
        var primaryDir = DirectoryOf(primary.Path);

        foreach (var name in Companions) {
            RemoteFile best     = default;
            var       bestRank = int.MaxValue;

            foreach (var file in files) {
                if (!string.Equals(FileName(file.Path), name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var dir = DirectoryOf(file.Path);

                var rank = string.Equals(dir, primaryDir, StringComparison.OrdinalIgnoreCase)
                    ? 0 // same dir as weights
                    : dir.Length == 0
                        ? 1 // repo root
                        : 2 + DepthOf(file.Path);

                if (string.IsNullOrEmpty(best.Path) || rank < bestRank ||
                    (rank == bestRank && string.CompareOrdinal(file.Path, best.Path) < 0)) {
                    best     = file;
                    bestRank = rank;
                }
            }

            if (!string.IsNullOrEmpty(best.Path) &&
                !planned.Any(f => string.Equals(f.Path, best.Path, StringComparison.OrdinalIgnoreCase)))
                planned.Add(best);
        }

        return new ModelPlan(primary, planned);
    }

    static string FileName(string path) {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    static string DirectoryOf(string path) {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    static int DepthOf(string path) {
        var depth = 0;

        foreach (var c in path)
            if (c == '/')
                depth++;

        return depth;
    }
}