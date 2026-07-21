namespace Kurrent.Surge.AI.HuggingFace;

public sealed class HuggingFaceHub(HuggingFaceClient client, string cacheDirectory) {
    static readonly IReadOnlyList<IModelLayout> Layouts = [new OnnxLayout(), new GgufLayout(), new SafetensorsLayout()];

    /// <summary>
    /// Standalone entry point: builds a <see cref="HuggingFaceClient"/> authenticated with the resolved Hugging
    /// Face token (sent as a <c>Bearer</c> credential) and resolves the cache root from <paramref name="options"/>.
    /// </summary>
    public HuggingFaceHub(HubOptions options) : this(
        new(options.Token ?? Environment.GetEnvironmentVariable("HF_TOKEN")), ResolveCacheRoot(options.CacheDirectory)) { }

    /// <summary>The resolved local cache root every download and cache lookup goes through.</summary>
    public string CacheDirectory => cacheDirectory;

    /// <summary>
    /// Scans <see cref="CacheDirectory"/> for cached model snapshots (<c>models--{owner}--{name}/snapshots/{revision}</c>),
    /// one entry per cached revision. A missing or empty cache root returns an empty list; never throws for that.
    /// Ordered by <see cref="CachedModel.Repo"/>, then <see cref="CachedModel.Revision"/>.
    /// </summary>
    public IReadOnlyList<CachedModel> ListCachedModels() {
        if (!Directory.Exists(cacheDirectory))
            return [];

        List<CachedModel> models = [];

        foreach (var modelDir in Directory.EnumerateDirectories(cacheDirectory, "models--*")) {
            var snapshotsDir = Path.Combine(modelDir, "snapshots");

            if (!Directory.Exists(snapshotsDir))
                continue;

            var repo = UnmangleRepo(Path.GetFileName(modelDir));

            foreach (var snapshotDir in Directory.EnumerateDirectories(snapshotsDir)) {
                var files      = Directory.EnumerateFiles(snapshotDir, "*", SearchOption.AllDirectories).ToList();
                var totalBytes = files.Sum(f => new FileInfo(f).Length);

                models.Add(new CachedModel(repo, Path.GetFileName(snapshotDir), snapshotDir, files.Count, totalBytes));
            }
        }

        return models.OrderBy(m => m.Repo, StringComparer.Ordinal).ThenBy(m => m.Revision, StringComparer.Ordinal).ToList();

        // "models--owner--name" -> "owner/name": a single "--" marks where the repo id's one '/' was mangled.
        static string UnmangleRepo(string directoryName) {
            var mangled   = directoryName["models--".Length..];
            var separator = mangled.IndexOf("--", StringComparison.Ordinal);

            return separator < 0 ? mangled : $"{mangled[..separator]}/{mangled[(separator + 2)..]}";
        }
    }

    public async ValueTask<ModelBundle> InspectModelAsync(ModelRef reference, CancellationToken ct = default) {
        var files = await client.ListFilesAsync(reference.Url, ct);

        var layout = ResolveLayout(reference, files);
        var plan   = layout.Select(files, reference);

        // Resolve every planned listing entry into a concrete download job. Files land under the HF-CLI snapshot
        // dir; companions picked from outside the primary's directory are relocated beside it, so a loader finds
        // the whole bundle in one place.
        var snapshot   = SnapshotDirectory(reference);
        var primaryDir = DirectoryOf(plan.Primary.Path);

        List<ModelFile> bundleFiles = [];

        foreach (var file in plan.Files)
            bundleFiles.Add(
                new ModelFile {
                    LocalPath = LocalPathFor(snapshot, primaryDir, file.Path),
                    RepoPath  = file.Path,
                    // Raw /resolve/ URL, built endpoint-aware so mirrors / enterprise hosts resolve correctly.
                    Url    = reference.ResolveUrl(file.Path),
                    Size   = file.Size,
                    Sha256 = file.Sha256
                });

        // Cached only when every file is already present at the expected size — a fast stat-only check.
        // Per-file SHA-256 integrity is enforced later, at download time.
        var cached = bundleFiles.All(f => File.Exists(f.LocalPath) && new FileInfo(f.LocalPath).Length == f.Size);

        return new ModelBundle(
            client, reference, bundleFiles,
            cached);

        static IModelLayout ResolveLayout(ModelRef reference, IReadOnlyList<RemoteFile> files) {
            if (reference.Kind is not ModelKind.Unknown)
                return Layouts.First(l => l.Kind == reference.Kind);

            // Auto-detect from the listing when the reference didn't pin a format.
            foreach (var layout in Layouts)
                if (files.Any(f => MatchesKind(f.Path, layout.Kind)))
                    return layout;

            throw new ModelException($"Could not determine a model format (onnx / gguf / safetensors) in '{reference.Repo}'.");

            static bool MatchesKind(string path, ModelKind kind) =>
                kind switch {
                    ModelKind.Onnx        => path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase),
                    ModelKind.Gguf        => path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase),
                    ModelKind.Safetensors => path.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase),
                    _                     => false
                };
        }
    }

    // HF-CLI-compatible snapshot dir: {root}/models--{owner}--{name}/snapshots/{revision} — the same on-disk
    // shape the Hugging Face CLI produces, so the cache root can be shared with an existing HF cache.
    string SnapshotDirectory(ModelRef reference) =>
        Path.Combine(
            cacheDirectory, "models--" + reference.Repo.Replace("/", "--"), "snapshots",
            reference.Revision);

    // Files under the primary's directory keep their repo sub-path; companions picked from elsewhere are
    // relocated beside the primary so loaders find the whole bundle in one directory.
    static string LocalPathFor(string snapshotDir, string primaryDir, string filePath) {
        var underPrimary = primaryDir.Length == 0
            ? !filePath.Contains('/')
            : filePath.StartsWith(primaryDir + "/", StringComparison.OrdinalIgnoreCase);

        var relative = underPrimary
            ? filePath
            : primaryDir.Length == 0
                ? FileName(filePath)
                : primaryDir + "/" + FileName(filePath);

        return Path.Combine(snapshotDir, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    static string FileName(string path) {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    static string DirectoryOf(string path) {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    // Cache root precedence: explicit > HF_HUB_CACHE (the official Hugging Face Hub cache variable,
    // https://huggingface.co/docs/huggingface_hub/package_reference/environment_variables) >
    // ~/.cache/huggingface/hub (the Hub's own default — same location the hf CLI and Python tooling use).
    static string ResolveCacheRoot(string? explicitRoot) {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
            return Path.GetFullPath(explicitRoot);

        var env = Environment.GetEnvironmentVariable("HF_HUB_CACHE");

        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            home = Directory.GetCurrentDirectory();

        return Path.GetFullPath(Path.Combine(home, ".cache", "huggingface", "hub"));
    }
}
