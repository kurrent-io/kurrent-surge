namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>
/// <see cref="IModelLayout"/> for ONNX bundles. Resolves the primary <c>.onnx</c> named by
/// <see cref="ModelRef.FilePath"/> and the companions that must sit next to it — external-data weight
/// shards, tokenizer, and config — so the consuming ONNX loader finds a complete bundle in one directory.
/// </summary>
/// <remarks>
/// Real ONNX LLM repos keep weights out-of-band as "external data" when the graph exceeds the 2 GB protobuf
/// limit. The companion blobs live in the <em>same directory</em> as the <c>.onnx</c> and are named with one
/// of several conventions (no single standard across exporters):
/// <list type="bullet">
///   <item><c>&lt;model&gt;.onnx.data</c> (ORT / Optimum default)</item>
///   <item><c>&lt;model&gt;.onnx_data</c> (older Optimum / some HF exports)</item>
///   <item><c>&lt;model&gt;.onnx.data_0</c>, <c>&lt;model&gt;.onnx.data_1</c>, … (sharded external data)</item>
///   <item>plain <c>model.onnx_data</c> next to <c>model.onnx</c></item>
/// </list>
/// All of these are matched, and every external-data blob in the model's directory is included to be safe
/// against naming drift.
/// </remarks>
public sealed class OnnxLayout : IModelLayout {
    const string OnnxExtension = ".onnx";

    /// <summary>Config artifacts the ONNX loader may need, in stable priority order.</summary>
    static readonly string[] ConfigNames = ["config.json", "genai_config.json", "generation_config.json", "preprocessor_config.json"];

    /// <summary>Tokenizer artifacts the ONNX loader may need, in stable priority order.</summary>
    static readonly string[] TokenizerNames = [
        "tokenizer.json",
        "tokenizer.model",
        "tokenizer_config.json",
        "vocab.json",
        "vocab.txt",
        "merges.txt",
        "special_tokens_map.json",
        "added_tokens.json",
        "spiece.model",
        "sentencepiece.bpe.model"
    ];

    /// <inheritdoc />
    public ModelKind Kind => ModelKind.Onnx;

    /// <inheritdoc />
    public ModelPlan Select(IReadOnlyList<RemoteFile> files, ModelRef reference) {
        var primary = files.FirstOrDefault(f => string.Equals(f.Path, reference.FilePath, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(primary.Path))
            throw new ModelException($"Model file '{reference.FilePath}' was not found in '{reference.Repo}'.");

        // Map back to the listing's RemoteFile so the plan carries size/sha for the download + integrity step.
        var byPath = new Dictionary<string, RemoteFile>(StringComparer.Ordinal);

        foreach (var file in files)
            byPath[file.Path] = file;

        var planned = new List<RemoteFile> { primary };

        foreach (var companion in CompanionFiles(files, primary.Path))
            planned.Add(byPath[companion]);

        return new ModelPlan(primary, planned);
    }

    #region ->> Companion selection <<-

    /// <summary>
    /// Returns the repo-relative paths (excluding the <c>.onnx</c> itself) that must be downloaded next to
    /// <paramref name="onnxRepoPath"/>: external-data shards (model-associated first, then any same-dir
    /// external data), tokenizer files, then config files. De-duplicated, deterministically ordered.
    /// </summary>
    static IReadOnlyList<string> CompanionFiles(IReadOnlyList<RemoteFile> files, string onnxRepoPath) {
        var onnxPath = Normalize(onnxRepoPath);
        var onnxDir  = DirectoryOf(onnxPath);
        var onnxName = FileName(onnxPath);

        var result = new List<string>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path) {
            if (string.Equals(Normalize(path), onnxPath, StringComparison.OrdinalIgnoreCase))
                return; // never include the .onnx itself

            if (seen.Add(Normalize(path)))
                result.Add(path);
        }

        // 1) External-data blobs in the SAME directory as the .onnx — model-associated ones first, then the
        //    rest (safe against naming drift).
        var sameDirExternal = new List<string>();

        foreach (var file in files) {
            var p = Normalize(file.Path);

            if (!string.Equals(DirectoryOf(p), onnxDir, StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsExternalDataName(FileName(p)))
                sameDirExternal.Add(file.Path);
        }

        sameDirExternal.Sort((a, b) => {
            var am = IsExternalDataFor(FileName(Normalize(a)), onnxName);
            var bm = IsExternalDataFor(FileName(Normalize(b)), onnxName);

            if (am != bm)
                return am ? -1 : 1;

            return string.CompareOrdinal(Normalize(a), Normalize(b));
        });

        foreach (var p in sameDirExternal)
            Add(p);

        // 2) Tokenizer files, then 3) config files: prefer same dir, then repo root, then shallowest.
        AddNamedArtifacts(
            files, TokenizerNames, onnxDir,
            Add);

        AddNamedArtifacts(
            files, ConfigNames, onnxDir,
            Add);

        return result;
    }

    /// <summary>
    /// For each wanted name, picks the best-located copy (same dir &gt; repo root &gt; shallowest &gt; ordinal)
    /// and adds it. Names are processed in their declared priority order.
    /// </summary>
    static void AddNamedArtifacts(IReadOnlyList<RemoteFile> files, string[] wantedNames, string onnxDir, Action<string> add) {
        foreach (var name in wantedNames) {
            string? best     = null;
            var     bestRank = int.MaxValue;

            foreach (var file in files) {
                var p = Normalize(file.Path);

                if (!string.Equals(FileName(p), name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var dir = DirectoryOf(p);

                var rank = string.Equals(dir, onnxDir, StringComparison.OrdinalIgnoreCase)
                    ? 0 // same dir
                    : dir.Length == 0
                        ? 1 // repo root
                        : 2 + DepthOf(p);

                if (best is null || rank < bestRank ||
                    (rank == bestRank && string.CompareOrdinal(p, Normalize(best)) < 0)) {
                    best     = file.Path;
                    bestRank = rank;
                }
            }

            if (best is not null)
                add(best);
        }
    }

    /// <summary>
    /// True if <paramref name="fileName"/> looks like ONNX external-data for any model:
    /// <c>*.onnx.data</c>, <c>*.onnx_data</c>, and sharded <c>*.onnx.data_N</c> / <c>*.onnx_data_N</c>.
    /// </summary>
    static bool IsExternalDataName(string fileName) {
        var dot = fileName.IndexOf(".onnx.data", StringComparison.OrdinalIgnoreCase);

        if (dot >= 0 && IsDataTail(fileName, dot + ".onnx.data".Length))
            return true;

        var underscore = fileName.IndexOf(".onnx_data", StringComparison.OrdinalIgnoreCase);

        return underscore >= 0 && IsDataTail(fileName, underscore + ".onnx_data".Length);
    }

    /// <summary>
    /// True if <paramref name="fileName"/> is external data for <paramref name="onnxName"/> specifically —
    /// <c>&lt;onnxName&gt;.data</c> (ORT) or <c>&lt;onnxName&gt;_data</c> (older Optimum), plus shard suffixes.
    /// </summary>
    static bool IsExternalDataFor(string fileName, string onnxName) {
        if (!onnxName.EndsWith(OnnxExtension, StringComparison.OrdinalIgnoreCase))
            return false;

        return StartsWithAndDataTail(fileName, $"{onnxName}.data")
            || StartsWithAndDataTail(fileName, $"{onnxName}_data");
    }

    static bool StartsWithAndDataTail(string fileName, string prefix) =>
        fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && IsDataTail(fileName, prefix.Length);

    /// <summary>
    /// True when the chars after a matched <c>.onnx.data</c> / <c>.onnx_data</c> token are empty or a shard
    /// suffix like <c>_0</c>, <c>_12</c>, <c>.0</c>, <c>-1</c>.
    /// </summary>
    static bool IsDataTail(string s, int start) {
        if (start == s.Length)
            return true; // exact ".onnx.data" / ".onnx_data"

        var sep = s[start];

        if (sep != '_' && sep != '.' && sep != '-')
            return false;

        if (start + 1 >= s.Length)
            return false;

        for (var i = start + 1; i < s.Length; i++)
            if (!char.IsAsciiDigit(s[i]))
                return false;

        return true;
    }

    #endregion // Companion selection

    #region ->> Path helpers <<-

    /// <summary>Everything before the last <c>/</c>, or "" if none.</summary>
    static string DirectoryOf(string path) {
        var p     = Normalize(path);
        var slash = p.LastIndexOf('/');
        return slash < 0 ? "" : p[..slash];
    }

    /// <summary>The final path segment (the file name).</summary>
    static string FileName(string path) {
        var p     = Normalize(path);
        var slash = p.LastIndexOf('/');
        return slash < 0 ? p : p[(slash + 1)..];
    }

    static int DepthOf(string path) {
        var depth = 0;

        foreach (var c in path)
            if (c == '/')
                depth++;

        return depth;
    }

    /// <summary>Normalises separators to <c>/</c> and trims a leading <c>./</c> or <c>/</c>.</summary>
    static string Normalize(string path) {
        if (string.IsNullOrEmpty(path))
            return "";

        var p = path.Replace('\\', '/').Trim();

        while (p.StartsWith("./", StringComparison.Ordinal))
            p = p[2..];

        while (p.StartsWith('/'))
            p = p[1..];

        return p;
    }

    #endregion // Path helpers
}