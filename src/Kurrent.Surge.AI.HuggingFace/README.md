# Kurrent.Surge.AI.HuggingFace

Hugging Face model **acquisition** for .NET: point at a model file's blob URL, plan its full artifact
set (weights + tokenizer + config), and download it — resumable, integrity-checked, and laid out
exactly like the Hugging Face CLI's own cache. Pure managed HTTP, no `hf` CLI, no Python, no external
process, AOT-friendly.

## Getting it

This library is not published as a standalone NuGet package. It ships inside the
[`surge` command-line tool](../Kurrent.Surge.Cli/README.md) — the product surface for model
acquisition — and is consumed in-repo via `ProjectReference`.

```shell
dotnet tool install -g Kurrent.Surge.Cli
```

## Quick start

The one-liners cover the common case — the `HF_TOKEN` environment variable and the shared Hugging
Face cache are picked up automatically:

```csharp
using Kurrent.Surge.AI.HuggingFace;

// Plan the bundle without downloading a byte.
var bundle = await HuggingFaceHub.InspectAsync(
    "https://huggingface.co/Xenova/all-MiniLM-L6-v2/blob/main/onnx/model_quantized.onnx");

Console.WriteLine($"{bundle.Files.Count} files, {bundle.TotalBytes.Humanize()}, cached: {bundle.Cached}");

// Inspect + download in one call. Already-valid cached files are skipped.
var downloaded = await HuggingFaceHub.DownloadAsync(url);
```

For control over the cache location and token, construct the hub yourself:

```csharp
var hub = new HuggingFaceHub(new HubOptions {
    CacheDirectory = "/var/cache/models",
    Token          = "hf_xxx"
});

var bundle = await hub.InspectModelAsync(ModelRef.From(url));
await bundle.DownloadAsync();
```

Every planned file carries its final on-disk location:

```csharp
foreach (var file in bundle.Files)
    Console.WriteLine($"{file.RepoPath} -> {file.LocalPath} ({file.Size.Humanize()})");
```

## Model references

`ModelRef` accepts exactly one shape: a **direct model-file URL** — the `/blob/` or `/resolve/` link
you copy from the Hugging Face UI:

```text
https://huggingface.co/{owner}/{repo}/blob/{revision}/{path/to/model.onnx}
https://huggingface.co/{owner}/{repo}/resolve/{revision}/{path/to/model.gguf}
```

Bare repository URLs (`…/owner/repo`, `…/owner/repo/tree/main`) are rejected — the reference must
name which file you mean; the layout machinery derives everything else. Non-model file extensions are
rejected for the same reason. Enterprise hosts and mirrors work: the endpoint is taken from the URL
itself, and download URLs are rebuilt endpoint-aware.

## Listing files without downloading

`HuggingFaceHub` wraps the lower-level `HuggingFaceClient`, which you can use directly when you only
want the repository tree. Listing streams the Hub's paginated tree API — large repos are enumerated
without buffering the whole listing:

```csharp
var client = new HuggingFaceClient();
var files  = await client.ListFilesAsync(url);

foreach (var file in files)
    Console.WriteLine($"{file.Path} ({file.Size} bytes, sha256: {file.Sha256 ?? "—"})");
```

## Authentication

For gated or private repositories, pass a token explicitly or rely on the `HF_TOKEN` environment
variable — the same variable the Hugging Face CLI uses. Every request sends it as a `Bearer`
credential. Public models need no token.

```csharp
var hub = new HuggingFaceHub(new HubOptions { Token = "hf_xxx" });   // explicit
var hub = new HuggingFaceHub(new HubOptions());                      // falls back to HF_TOKEN
```

## The cache

### Location

The cache root resolves in this order:

| Priority | Source                     | Notes                                                        |
|----------|----------------------------|--------------------------------------------------------------|
| 1        | `HubOptions.CacheDirectory` | explicit override                                            |
| 2        | `HF_HUB_CACHE`             | the [official Hub cache variable](https://huggingface.co/docs/huggingface_hub/package_reference/environment_variables) |
| 3        | `~/.cache/huggingface/hub` | the Hub's own default                                        |

That default **is** the shared Hugging Face cache — the same location the `hf` CLI and Python
`huggingface_hub` use. Models downloaded by either side are visible to both. Point `CacheDirectory`
elsewhere to keep this package's downloads separate.

### Layout

Files land in the HF-CLI snapshot shape:

```text
{cacheRoot}/models--{owner}--{name}/snapshots/{revision}/…
```

Companions planned from *outside* the primary file's directory are relocated beside it, so a loader
finds the complete bundle in one directory — no chasing files across the repo tree.

### Validation and self-healing

A file already on disk at the expected size — and, when the listing carried an LFS `oid`, hashing to
it — counts as cached and is skipped. A same-size but corrupted file fails the SHA-256 check and is
re-fetched automatically. `force: true` re-downloads everything regardless:

```csharp
await bundle.DownloadAsync(force: true);
```

### Enumerating the cache

```csharp
foreach (var model in hub.ListCachedModels())
    Console.WriteLine($"{model.Repo}@{model.Revision}: {model.FileCount} files, {model.TotalBytes} bytes");
```

`CachedModel.Path` is the snapshot directory — the handle for eviction or inspection. The `surge`
CLI's `cache list` / `cache clear` / `models clear` commands are built on exactly this.

## Format layouts

Inspecting a model picks an `IModelLayout` by the extension named in the URL — or, when the URL
doesn't pin a known format, by detecting one from the repository listing. The layout decides which
companion files travel with the weights. Everything below is behavior, not aspiration — each rule is
integration-tested against real Hugging Face repositories.

### ONNX

The primary is the exact `.onnx` your URL names. Real ONNX repos keep large weights out-of-band as
**external data** once the graph exceeds protobuf's 2 GB limit, and exporters disagree on naming —
all of these conventions are matched:

| Pattern                    | Origin                          |
|----------------------------|---------------------------------|
| `{model}.onnx.data`        | ONNX Runtime / Optimum default  |
| `{model}.onnx_data`        | older Optimum and some HF exports |
| `{model}.onnx.data_0`, `_1`, … | sharded external data       |

Every external-data blob in the model's directory is included — deliberately generous, so naming
drift between exporters can't silently produce an incomplete bundle. Blobs named for your model sort
first.

Tokenizer artifacts (`tokenizer.json`, `tokenizer.model`, `vocab.json`, `merges.txt`,
`sentencepiece.bpe.model`, …) and config artifacts (`config.json`, `genai_config.json`,
`generation_config.json`, `preprocessor_config.json`) are then added, each resolved by location rank:
**same directory as the weights → repo root → shallowest path**. That rank matters on repos that
ship nested module sidecars — a `1_Pooling/config.json` must never shadow the root `config.json`.

Quantized exports often ship per-CPU variants (`model_qint8_avx512_vnni.onnx`,
`model_quint8_avx2.onnx`, …). `HardwareProbe` tells you which to pick:

```csharp
var tag    = HardwareProbe.PreferredArchTags().First();   // e.g. "arm64", "avx512_vnni"
var bundle = await HuggingFaceHub.InspectAsync(
    $"https://huggingface.co/acme/model/blob/main/onnx/model_{tag}.onnx");
```

Detection is pure (`System.Runtime.Intrinsics`, AOT-safe) with no policy attached — the choice stays
yours. The `surge huggingface probe` command prints the same tags.

### GGUF

GGUF (llama.cpp / Ollama) is self-contained — tokenizer and metadata live inside the file — so the
bundle is deliberately lean. A GGUF repo typically holds one model at several quantizations; there is
no auto-selection: **the URL is the selection**. Point at `…Q4_K_M.gguf` and that is what you get.

Large quants ship split across numbered shards. Naming any shard of
`model-00001-of-00003.gguf` expands the plan to the **entire ordered shard set**, with the first
shard as the primary. An incomplete set in the listing is an error, not a partial download:

```text
ModelException: Split GGUF 'model-*-of-00003.gguf' in 'owner/repo' is incomplete: found 2 of 3 shards.
```

A sibling `tokenizer.json` / `config.json` rides along when present — best-effort: GGUF doesn't need
them, so their absence is not an error.

### Safetensors

Two cases, decided by the listing:

- **Single file** — the primary is the `.safetensors` your URL names, alone.
- **Sharded** — when a `*.safetensors.index.json` is present, the **index becomes the primary** (it
  is what a loader opens) and every `.safetensors` shard in the repo joins the plan. Shards are
  discovered from the listing rather than by parsing the index's `weight_map` — verified equivalent
  on real sharded repos (Qwen2.5-7B, Phi-3-mini, zephyr-7b), which keeps planning pure: no
  download-then-parse step.

Config and tokenizer companions (`config.json`, `tokenizer.json`, `tokenizer.model`, `vocab.json`,
`merges.txt`, …) use the same location rank as ONNX: same directory → repo root → shallowest. This
rule exists because of a real bug class: on `sentence-transformers` repos, the nested
`1_Pooling/config.json` used to shadow the root `config.json` — and since the download flattens the
bundle beside the primary, the bundle shipped the wrong config contents. The rank is guarded by
integration tests against those exact repos.

## Downloading

The transport is the [`Downloader`](https://github.com/bezzad/Downloader) engine: each file downloads
chunk-parallel with automatic resume; files within a bundle proceed sequentially, so per-file
progress stays coherent. After each download, the SHA-256 from the listing's LFS `oid` is verified —
a mismatch throws instead of leaving a poisoned cache entry.

### Progress

`DownloadAsync` reports per-file progress through the standard `IProgress<DownloadProgress>`:

```csharp
public readonly record struct DownloadProgress(
    string File, long BytesDownloaded, long? TotalBytes, int FileIndex, int FileCount);
```

`IProgressTracker` is the pluggable seam (it *is* an `IProgress<DownloadProgress>`), and
`LoggerProgressTracker` is the built-in implementation for `Microsoft.Extensions.Logging` — it logs
file start, each crossed step (10% by default), and completion, instead of flooding the sink with
per-chunk noise:

```csharp
await bundle.DownloadAsync(new LoggerProgressTracker(logger));
```

```text
info: Downloading onnx/model_quantized.onnx (1/6): 10% — 2,703,360/27,033,600 bytes
info: Downloading onnx/model_quantized.onnx (1/6): 20% — 5,406,720/27,033,600 bytes
```

The `surge` CLI's live progress bars are just another `IProgressTracker` (`SpectreProgressTracker`),
which is the intended pattern for custom sinks.

## What's inside

| Type | Role |
|------|------|
| `ModelRef` | parses and validates a direct model-file URL |
| `HuggingFaceClient` | low-level transport: tree listing, file downloads |
| `HuggingFaceHub` / `HubOptions` | the entry point: inspect + plan + download + cache enumeration |
| `ModelBundle` / `ModelFile` | the planned download set and its cache/download logic |
| `IModelLayout` + `OnnxLayout` / `GgufLayout` / `SafetensorsLayout` | per-format bundle planning |
| `IProgressTracker` / `LoggerProgressTracker` | pluggable download progress |
| `ByteSizeExtensions` | `bytes.Humanize()` — human-friendly sizes for the byte counts above |
| `HardwareProbe` | per-CPU variant tag detection, no policy |

Native-AOT-clean throughout — BCL + `Downloader` + `Microsoft.Extensions.Logging.Abstractions` only.

## Scope

This package **acquires** model files — it does not train or run inference. Loading a downloaded
ONNX/GGUF/Safetensors model into a runtime is out of scope; pair it with `Microsoft.ML.OnnxRuntime`,
`LLamaSharp`, or whatever inference stack you're already using.

## Links

- [The surge CLI](../Kurrent.Surge.Cli/README.md)
- [Documentation](https://kurrent.io/docs)
- [GitHub Repository](https://github.com/kurrent-io/kurrent-sdk-dotnet)
- [License (Apache 2.0)](https://github.com/kurrent-io/kurrent-sdk-dotnet/blob/main/LICENSE.md)
