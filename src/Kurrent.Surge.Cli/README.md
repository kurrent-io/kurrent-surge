# Kurrent.Surge.Cli

`surge` is the Kurrent Surge command-line tool. Its first vertical is `huggingface`: Hugging Face model
acquisition over [`Kurrent.Surge.AI.HuggingFace`](https://github.com/kurrent-io/kurrent-sdk-dotnet).

Inspect a model's planned bundle, download it with resume and integrity checking, and manage the local
model cache — all from the terminal.

## Installation

```shell
dotnet tool install -g Kurrent.Surge.Cli
```

Requires the .NET 10 runtime.

## Models

Operations on Hugging Face models: inspect, download, and remove from the local cache.

### `models inspect`

Lists a model's planned bundle — weights, tokenizer, and config companions — without downloading anything.
Pass `--json` to get the same answer as machine-readable JSON on stdout.

```shell
surge huggingface models inspect https://huggingface.co/Xenova/all-MiniLM-L6-v2/blob/main/onnx/model_quantized.onnx

surge huggingface models inspect <url> --json
```

### `models download`

Inspects the model, then downloads its full bundle with per-file progress bars. Files already cached and
intact are skipped; `--force` re-downloads them anyway. `--quiet` drops the progress UI and prints a single
summary line — the right mode for CI.

```shell
surge huggingface models download <url>

surge huggingface models download <url> --quiet --force
```

### `models clear`

Clears one cached model — every cached revision — identified by the exact repo id that `cache list` prints.
There is no prompt: naming the model is the confirmation. This only touches the local cache; the Hub is
never contacted.

```shell
surge huggingface models clear Xenova/all-MiniLM-L6-v2
```

## Cache

Operations on the local model cache as a whole.

### `cache`

The bare invocation prints the resolved cache root and nothing else. The output is plain stdout, safe for
shell substitution.

```shell
surge huggingface cache
```

### `cache list`

Lists every model cached locally, with per-snapshot file counts and sizes.

```shell
surge huggingface cache list
```

### `cache clear`

Deletes every cached model. Interactive terminals get a confirmation prompt showing the count, total size,
and cache root. Non-interactive contexts (CI) must pass `--yes`, otherwise the command refuses and exits 1
without deleting anything.

```shell
surge huggingface cache clear --yes
```

## Probe

Prints the CPU architecture tags this host supports, best-first (`avx512_vnni`, `avx512`, `avx2`, `arm64`).
Use it to pick the right quantized ONNX variant for the machine.

```shell
surge huggingface probe
```

## Authentication

Commands that talk to the Hub accept `--token`. When the option is absent, the `HF_TOKEN` environment
variable is used automatically. Public models need no token at all.

## Cache location

Every cache-touching command accepts `--cache-dir`. When the option is absent, the root resolves exactly
like the official Hugging Face tooling:

1. `HF_HUB_CACHE` environment variable
2. `~/.cache/huggingface/hub`

That default is the same shared cache the `hf` CLI and the Python libraries use, so models downloaded by
either side are visible to both. `cache clear` and `models clear` therefore manage that shared cache — they
only ever touch `models--*` entries, never datasets, spaces, or unrelated files.

## Links

- [Documentation](https://kurrent.io/docs)
- [GitHub Repository](https://github.com/kurrent-io/kurrent-sdk-dotnet)
- [License (Apache 2.0)](https://github.com/kurrent-io/kurrent-sdk-dotnet/blob/main/LICENSE.md)
