# Kurrent.Surge.Cli

`surge` — the Kurrent Surge command-line tool. The first vertical is `huggingface`: Hugging Face model
acquisition over [`Kurrent.Surge.AI.HuggingFace`](https://github.com/kurrent-io/kurrent-sdk-dotnet). Inspect a
model's planned bundle, download it with resume + integrity checking, or just ask where the local cache lives.

## Installation

```shell
dotnet tool install -g Kurrent.Surge.Cli
```

### Supported Frameworks

- .NET 10.0

## Usage

```shell
# List a model's planned bundle (weights + tokenizer + config) without downloading it.
surge huggingface models inspect https://huggingface.co/Xenova/all-MiniLM-L6-v2/blob/main/onnx/model_quantized.onnx

# Same, as machine-readable JSON.
surge huggingface models inspect <url> --json

# Download the bundle, with a live per-file progress display.
surge huggingface models download <url>

# Download quietly — one summary line, no progress UI. Useful in CI/CD.
surge huggingface models download <url> --quiet --force

# Clear one cached model (every revision) by the exact repo id `cache list` prints. No prompt, no
# --yes — naming the model is the confirmation. Only touches the local cache, never the Hub.
surge huggingface models clear Xenova/all-MiniLM-L6-v2

# Print the resolved local cache root — the bare `cache` invocation, no subcommand.
surge huggingface cache

# List every model cached locally, with per-snapshot file counts and sizes.
surge huggingface cache list

# Delete every cached model. Interactive terminals get a confirmation prompt; everywhere else (CI)
# needs --yes, or the command refuses and exits 1 without deleting anything.
surge huggingface cache clear --yes

# Print the CPU arch tags this host supports, best-first (avx512_vnni / avx512 / avx2 / arm64).
surge huggingface probe
```

`--token` (falls back to the `HF_TOKEN` environment variable) and `--cache-dir` (falls back to
`HF_HUB_CACHE`, else `~/.cache/huggingface/hub` — the same shared cache the `hf` CLI and Python
tooling use) are available on every command that talks to the Hub or the cache. `cache clear` /
`models clear` therefore manage that shared cache by default (only `models--*` entries — see the
library's cache-behavior docs).

## Links

- [Documentation](https://kurrent.io/docs)
- [GitHub Repository](https://github.com/kurrent-io/kurrent-sdk-dotnet)
- [License (Apache 2.0)](https://github.com/kurrent-io/kurrent-sdk-dotnet/blob/main/LICENSE.md)
