# Kurrent.Surge.AI.HuggingFace

Hugging Face model **acquisition** for .NET: point at a model file's blob URL, plan its full artifact
set (weights + tokenizer + config), and download it — resumable, integrity-checked, and laid out
exactly like the Hugging Face CLI's own cache. Pure managed HTTP, no `hf` CLI, no Python, no external
process, AOT-friendly.

## Installation

```shell
dotnet add package Kurrent.Surge.AI.HuggingFace
```

### Supported Frameworks

- .NET 10.0

## Quick start

```csharp
using Kurrent.Surge.AI.HuggingFace;

var hub = new HuggingFaceHub(new HubOptions { CacheDirectory = "/var/cache/models" });

// Inspect: list the repo, auto-detect the format (ONNX/GGUF/Safetensors), plan the bundle — no bytes yet.
var bundle = await hub.InspectModelAsync(
    new ModelRef("https://huggingface.co/Xenova/all-MiniLM-L6-v2/blob/main/onnx/model_quantized.onnx"));

Console.WriteLine($"{bundle.Files.Count} files, {bundle.TotalBytes} bytes, cached: {bundle.Cached}");

// Download every file in the bundle (skips anything already valid in the cache).
await bundle.DownloadAsync();
```

Only a direct model-file URL is accepted — the `/blob/` or `/resolve/` link you'd copy from the
Hugging Face UI, e.g. `.../owner/repo/blob/main/onnx/model.onnx`. A bare repository URL
(`.../owner/repo` or `.../owner/repo/tree/main`) is rejected: `ModelRef` needs to know which file
you mean.

## Listing files without downloading

`HuggingFaceHub` wraps the lower-level `HuggingFaceClient`, which you can use directly if you just
want the repository tree:

```csharp
var client = new HuggingFaceClient();
var files  = await client.ListFilesAsync("https://huggingface.co/Xenova/all-MiniLM-L6-v2/blob/main/onnx/model.onnx");

foreach (var file in files)
    Console.WriteLine($"{file.Path} ({file.Size} bytes)");
```

## Authentication

For gated or private repositories, pass a token explicitly or rely on the `HF_TOKEN` environment
variable — the same variable the Hugging Face CLI uses:

```csharp
var hub = new HuggingFaceHub(new HubOptions { Token = "hf_xxx" });
// or: new HuggingFaceClient("hf_xxx");
```

If `Token` is left unset, `HuggingFaceHub` falls back to `HF_TOKEN`. Every request sends it as a
`Bearer` credential.

## Cache behavior

The default cache root **is** the shared Hugging Face cache — `~/.cache/huggingface/hub`, the same
location the `hf` CLI and Python `huggingface_hub` tooling use, in their exact on-disk shape
(`{cacheRoot}/models--{owner}--{name}/snapshots/{revision}/...`). Point `CacheDirectory` somewhere
else to keep this package's downloads separate from that shared cache instead. The root resolves, in
order: the explicit `CacheDirectory`, then `HF_HUB_CACHE` (the
[official Hugging Face Hub cache variable](https://huggingface.co/docs/huggingface_hub/package_reference/environment_variables)),
then `~/.cache/huggingface/hub`.

A file already on disk at the expected size — and, when the listing carried an LFS `oid`, hashing to
it — is treated as cached and skipped. A same-size but corrupted file is detected by the SHA-256
check and re-fetched automatically (self-healing cache). Pass `force: true` to `DownloadAsync` to
re-download everything regardless:

```csharp
await bundle.DownloadAsync(force: true);
```

## Per-format bundle planning

Inspecting a model auto-detects its format from the listing (or trusts the extension named in the
URL) and picks the right `IModelLayout` to decide which companion files travel with it:

- **`OnnxLayout`** — the named `.onnx`, plus any external-data shards next to it
  (`*.onnx.data`, `*.onnx_data`, sharded variants), plus tokenizer and config files.
- **`GgufLayout`** — the named `.gguf`, expanded to every shard of a split quant
  (`model-00001-of-00003.gguf` …), plus an optional sibling `tokenizer.json`/`config.json`.
- **`SafetensorsLayout`** — a single `.safetensors` file, or — when a
  `*.safetensors.index.json` is present — every shard it indexes, plus tokenizer/config
  companions.

## Choosing a per-CPU variant

`HardwareProbe.PreferredArchTags()` reports which quantization tags the current host's CPU
supports, best-first (`avx512_vnni`, `avx512`, `avx2`, or `arm64`) — the same tags Optimum uses when
naming per-hardware ONNX exports. It's a pure detection utility with no policy attached; pick the
file yourself:

```csharp
var tag = HardwareProbe.PreferredArchTags().First();
var bundle = await hub.InspectModelAsync(
    new ModelRef($"https://huggingface.co/acme/model/blob/main/onnx/model_{tag}.onnx"));
```

## What's inside

- **`ModelRef`** — parses and validates a direct Hugging Face file URL
- **`HuggingFaceClient`** — low-level transport: list a repo's tree, download files
- **`HuggingFaceHub`** / **`HubOptions`** — the one-stop entry point: inspect + plan + download
- **`ModelBundle`** / **`ModelFile`** — the planned download set and its download/cache logic
- **`IModelLayout`** and its three implementations (`OnnxLayout`, `GgufLayout`, `SafetensorsLayout`)
- **`HardwareProbe`** — per-CPU ONNX variant tag detection (no policy)
- **AOT Compatible** — Native-AOT-clean, BCL + `Downloader` only

## Scope

This package **acquires** model files — it does not train or run inference. Loading a downloaded
ONNX/GGUF/Safetensors model into a runtime is out of scope; pair it with `Microsoft.ML.OnnxRuntime`,
`LLamaSharp`, or whatever inference stack you're already using.

## Links

- [Documentation](https://kurrent.io/docs)
- [GitHub Repository](https://github.com/kurrent-io/kurrent-sdk-dotnet)
- [License (Apache 2.0)](https://github.com/kurrent-io/kurrent-sdk-dotnet/blob/main/LICENSE.md)
