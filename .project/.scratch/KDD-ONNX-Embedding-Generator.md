# KDD: In-Process ONNX Embedding Generator for KurrentDB

| Field | Value |
|---|---|
| **KDD** | KDD-2026-003 |
| **Title** | In-Process ONNX Embedding Generator for KurrentDB |
| **Status** | Draft |
| **Authors** | DevEx Team |
| **Created** | 2026-03-01 |
| **Updated** | 2026-03-01 |

## Summary

This document describes the design and implementation of `Kurrent.Extensions.AI.Embeddings`, a library that provides in-process text embedding generation using ONNX Runtime. The library implements `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>`, enabling KurrentDB to generate semantic embeddings without external API dependencies. It supports multiple pre-trained models with automatic downloading from HuggingFace, pluggable tokenizers, and configurable pooling strategies.

The library is designed to power semantic search over event streams stored in DuckDB, complementing KurrentDB's distributed stream processing capabilities.

## Motivation

Event-sourced systems accumulate rich textual data in event payloads — order descriptions, user actions, error messages, audit entries. Traditional querying relies on exact field matching or full-text search, which misses semantic relationships. A user searching for "shipping updates" should find `OrderShipped`, `OrderDelivered`, and `TrackingNumberAssigned` events even when those exact words don't appear in the payload.

Embedding-based semantic search solves this. However, current approaches require external API calls (OpenAI, Cohere) or separate inference servers, adding latency, cost, and operational complexity that conflicts with KurrentDB's philosophy as a self-contained database.

### Goals

- Zero external dependencies: embeddings generated in-process, no API keys required
- Zero-config experience: one line of code to download a model and start generating embeddings
- Integration with `Microsoft.Extensions.AI` ecosystem for interoperability
- Model-agnostic architecture supporting BERT, XLM-RoBERTa, and future model families
- DuckDB VSS integration for vector similarity search over event streams
- Production-suitable resource footprint for an embedded database component

### Non-Goals

- Training or fine-tuning models
- GPU inference (CPU-only for the embedded use case; GPU is opt-in via SessionOptions)
- Replacing external embedding providers — the library complements them via the `IEmbeddingGenerator` abstraction
- Real-time embedding of high-throughput event streams (batch/async indexing is the intended pattern)

## Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Consumer Code                             │
│         IEmbeddingGenerator<string, Embedding<float>>        │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│                 OnnxEmbeddingGenerator                        │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────────┐  │
│  │  Tokenizer    │  │ ONNX Runtime │  │ Pooling Strategy   │  │
│  │  (pluggable)  │  │ InferenceSession│ │ Mean/CLS/LastToken│  │
│  └──────┬───────┘  └──────┬───────┘  └────────┬──────────┘  │
│         │                 │                    │              │
│  ┌──────▼─────────────────▼────────────────────▼──────────┐  │
│  │              OnnxEmbeddingGeneratorOptions               │  │
│  │  ModelPath · Tokenizer · Pooling · TensorNames · Dims   │  │
│  └─────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│              HuggingFaceModelResolver                        │
│  Downloads ONNX models + vocab files from HuggingFace Hub    │
│  Atomic writes · Local caching · Progress logging            │
│  Default cache: ~/.kurrent/models                            │
└──────────────────────────────────────────────────────────────┘
```

### Key Abstractions

**TextTokenizer** — A delegate `(string text, int maxSequenceLength) => TokenizedInput` that converts raw text into model-ready token arrays. Two implementations ship:

- `Tokenizers.Bert(vocabPath, lowerCase)` — WordPiece tokenizer for BERT-family models. Handles `[CLS]`/`[SEP]` tokens, attention masks, and padding.
- `Tokenizers.SentencePiece(modelPath)` — SentencePiece BPE tokenizer for XLM-RoBERTa-family models. Handles `<s>`/`</s>` tokens.

**PoolingStrategy** — Enum controlling how per-token hidden states are reduced to a single embedding vector:

- `Mean` — Attention-weighted average of all token embeddings (sentence-transformers default)
- `Cls` — Use the `[CLS]`/`<s>` token embedding only (BGE models)
- `LastToken` — Use the last token embedding (decoder-based models like Qwen3-Embedding)

**OnnxTensorNames** — Configurable input/output tensor names with presets for `Bert` (standard) and `XlmRoberta` (no token_type_ids).

**Strongly-typed ModelOptions** — Each supported model has a dedicated options class (e.g., `BgeSmallEnV15Options`, `MultilingualE5SmallOptions`) that sets all parameters correctly via `[SetsRequiredMembers]` constructors.

### Inference Pipeline

```
Input text
  → Tokenizer (text → input_ids, attention_mask, token_type_ids)
    → ONNX Runtime InferenceSession.Run()
      → Raw hidden states [1, seq_len, hidden_dim]
        → Pooling (Mean/CLS/LastToken → [1, hidden_dim])
          → L2 Normalization (optional)
            → Embedding<float>
```

### Dependency Graph

```
Kurrent.Extensions.AI.Embeddings
  ├── Microsoft.Extensions.AI.Abstractions (10.3.0)
  ├── Microsoft.ML.OnnxRuntime (1.21.0)
  ├── Microsoft.ML.Tokenizers (2.0.0)
  └── Microsoft.Extensions.Logging.Abstractions
```

No transitive dependency on Python, PyTorch, or any ML framework. Pure .NET.

## Supported Models

### English Models

| Model | Dims | Disk | RAM (rec) | Inference | MTEB | Pooling | Tokenizer | Default |
|---|---|---|---|---|---|---|---|---|
| bge-small-en-v1.5 | 384 | 130MB | ~250MB | ~2-3ms | ~66 | CLS | BERT | **English default** |
| bge-base-en-v1.5 | 768 | 440MB | ~680MB | ~4-6ms | ~68 | CLS | BERT | |
| all-MiniLM-L6-v2 | 384 | 87MB | ~200MB | ~1-2ms | ~63 | Mean | BERT | Lightweight fallback |
| all-MiniLM-L12-v2 | 384 | 130MB | ~250MB | ~2-3ms | ~64 | Mean | BERT | |
| all-mpnet-base-v2 | 768 | 420MB | ~650MB | ~4-6ms | ~65 | Mean | BERT | |
| paraphrase-MiniLM-L6-v2 | 384 | 87MB | ~200MB | ~1-2ms | ~62 | Mean | BERT | |

### Multilingual Models

| Model | Dims | Disk | RAM (rec) | Languages | MTEB | Pooling | Tokenizer | Default |
|---|---|---|---|---|---|---|---|---|
| multilingual-e5-small | 384 | 470MB | ~600MB | 100+ | ~62 | Mean | SentencePiece | **Multilingual default** |
| multilingual-e5-base | 768 | 1.1GB | ~1.4GB | 100+ | ~64 | Mean | SentencePiece | |
| BGE-M3 | 1024 | 2.2GB | ~2.8GB | 100+ | ~66+ | CLS | SentencePiece | Server-only |
| paraphrase-multilingual-MiniLM-L12-v2 | 384 | 470MB | ~700MB | 50+ | ~58 | Mean | BERT | Legacy |

### Default Model Rationale

**English: bge-small-en-v1.5** — 3 MTEB points above all-MiniLM-L6-v2 with identical vector storage cost (384 dims × 4 bytes = 1.5KB per event). 130MB disk and ~250MB RAM are acceptable for a database process. CLS pooling is simpler and marginally faster than mean pooling.

**Multilingual: multilingual-e5-small** — Same 384 dims as the English default so vector storage is consistent. 100+ language coverage via Microsoft's XLM-RoBERTa-based training. Well-maintained and widely adopted.

### Models Evaluated but Not Included

**Qwen3-Embedding-0.6B** — State-of-the-art quality (MTEB #1 at 8B scale) but fundamentally unsuitable for embedded use: 600M parameter decoder model requiring ~2.5GB RAM, 50-200ms CPU inference, and fragile ONNX export with KV cache dependencies. Better served as an external provider via Ollama/vLLM accessed through the standard `IEmbeddingGenerator` abstraction.

**EmbeddingGemma-300M** — Promising compact multilingual model from Google but ONNX Runtime support is still immature (missing `gemma3_text` backend support). Worth revisiting when tooling catches up.

## Usage

### Zero-Config (Recommended)

```csharp
// English — downloads bge-small-en-v1.5 on first run
using var generator = await OnnxEmbeddingGenerator.CreateDefaultAsync();

// Multilingual — downloads multilingual-e5-small on first run
using var generator = await OnnxEmbeddingGenerator.CreateDefaultMultilingualAsync();
```

### Specific Model

```csharp
using var generator = await OnnxEmbeddingGenerator.CreateAllMiniLmL6V2Async();
using var generator = await OnnxEmbeddingGenerator.CreateBgeM3Async();
```

### Custom Cache and Logging

```csharp
var resolver = new HuggingFaceModelResolver(
    cacheDir: "/opt/kurrent/models",
    logger: loggerFactory.CreateLogger<HuggingFaceModelResolver>()
);
using var generator = await OnnxEmbeddingGenerator.CreateDefaultAsync(resolver);
```

### Dependency Injection

```csharp
services.AddOnnxEmbeddingGenerator(new BgeSmallEnV15Options(modelPath, vocabPath));
```

### Full Manual Control

```csharp
var generator = new OnnxEmbeddingGenerator(new OnnxEmbeddingGeneratorOptions
{
    ModelPath          = "/path/to/custom-model.onnx",
    Tokenizer          = Tokenizers.SentencePiece("/path/to/spm.model"),
    EmbeddingDimension = 1024,
    MaxSequenceLength  = 512,
    Pooling            = PoolingStrategy.LastToken,
    Normalize          = true,
    TensorNames        = new OnnxTensorNames { TokenTypeIds = null },
    SessionOptions     = customSessionOptions
});
```

## DuckDB Vector Storage & Search

Embeddings are stored alongside event metadata in DuckDB using the fixed-size `ARRAY` type:

```sql
CREATE TABLE event_embeddings (
    stream_id    VARCHAR NOT NULL,
    event_number BIGINT NOT NULL,
    event_type   VARCHAR NOT NULL,
    payload_text VARCHAR,
    embedding    FLOAT[384],
    created_at   TIMESTAMP,
    PRIMARY KEY (stream_id, event_number)
);
```

### Querying (Brute-Force)

No extensions required. Works on any DuckDB instance:

```sql
SELECT stream_id, event_type,
       array_cosine_distance(embedding, $query_embedding::FLOAT[384]) AS distance
FROM event_embeddings
ORDER BY distance
LIMIT 10;
```

For 384-dim vectors, brute-force scans are sub-100ms up to ~500K rows on modern hardware.

### Querying (HNSW Index via VSS Extension)

For larger datasets, the experimental VSS extension provides HNSW indexing:

```sql
INSTALL vss;
LOAD vss;
SET hnsw_enable_experimental_persistence = true;

CREATE INDEX event_hnsw_idx
ON event_embeddings USING HNSW (embedding)
WITH (metric = 'cosine');
```

The optimizer rewrites `ORDER BY array_cosine_distance(...) LIMIT k` into an index scan.

### VSS Limitations

- **Persistence is experimental.** Full index serialization on every checkpoint; no incremental updates. Risk of corruption on unexpected shutdown.
- **No filtered search.** `WHERE stream_id = X ORDER BY distance LIMIT k` does not use the HNSW index. Requires a two-step CTE pattern.
- **Lazy deletes.** Deleted rows are marked but not removed from the index. Periodic `DROP INDEX` + `CREATE INDEX` needed.
- **In-memory only is safer.** Index lives in RAM and rebuilds on restart if persistence is disabled.

### Recommended Approach

Start with brute-force scans (no VSS dependency). Add HNSW indexing as opt-in for deployments exceeding ~500K embedded events. This avoids persistence fragility in the checkpointing layer.

## Production Readiness

### What Exists Today (Prototype)

- [x] `IEmbeddingGenerator<string, Embedding<float>>` implementation
- [x] ONNX Runtime inference with configurable SessionOptions
- [x] BERT WordPiece tokenizer (via `Microsoft.ML.Tokenizers`)
- [x] SentencePiece tokenizer (for XLM-RoBERTa models)
- [x] Three pooling strategies (Mean, CLS, LastToken)
- [x] Configurable tensor names (BERT, XLM-RoBERTa presets)
- [x] 11 pre-configured model options with strongly-typed constructors
- [x] Automatic model download from HuggingFace with local caching
- [x] Atomic downloads (write to `.tmp`, rename on completion)
- [x] Factory methods for zero-config setup
- [x] DI registration via `AddOnnxEmbeddingGenerator`
- [x] Demo application with semantic similarity and event stream search

### What's Needed for Production

#### P0 — Must Have

**Testing**

- [ ] Unit tests for each tokenizer (BERT, SentencePiece) with edge cases: empty strings, unicode, max-length truncation, special characters
- [ ] Unit tests for each pooling strategy with known-good reference outputs
- [ ] Integration tests verifying embedding output matches Python sentence-transformers for the same input/model (golden vectors)
- [ ] Integration tests for HuggingFace download — retry, corruption recovery, partial download cleanup
- [ ] Load tests measuring memory usage and latency under concurrent embedding requests
- [ ] Tests for ONNX model compatibility across ONNX Runtime versions

**Error Handling & Resilience**

- [ ] Graceful handling of corrupted model files (detect, delete cache, re-download)
- [ ] Timeout and retry policy for HuggingFace downloads (currently no timeout)
- [ ] Model file integrity verification (SHA256 checksums from HuggingFace)
- [ ] Clear error messages when model files are missing, incompatible, or wrong version
- [ ] Handle ONNX Runtime out-of-memory gracefully (log, suggest smaller model)

**Thread Safety & Concurrency**

- [ ] Verify `InferenceSession` thread safety (ONNX Runtime sessions are thread-safe for `Run()` but not for concurrent `Dispose()`)
- [ ] Add `SemaphoreSlim`-based concurrency control for batch operations to limit peak memory
- [ ] Ensure tokenizer instances are thread-safe or use pooling

**Resource Management**

- [ ] `IAsyncDisposable` implementation for proper cleanup
- [ ] Memory budget enforcement — reject embedding requests if estimated memory exceeds configured limit
- [ ] Warmup method to pre-load model into memory (avoid first-request latency spike)

#### P1 — Should Have

**Batching**

- [ ] Efficient batch embedding with padding to longest-in-batch (not max-sequence-length)
- [ ] Configurable batch size with auto-tuning based on available memory
- [ ] Progress reporting for large batch operations via `IProgress<T>`

**Observability**

- [ ] OpenTelemetry metrics: embeddings generated (counter), latency (histogram), model load time, cache hits/misses
- [ ] OpenTelemetry tracing: spans for tokenization, inference, pooling
- [ ] Health check endpoint: model loaded, memory usage, last inference latency
- [ ] Structured logging throughout (currently partial)

**DuckDB Integration**

- [ ] `EmbeddingIndexer` service that subscribes to event streams and maintains the DuckDB embedding table
- [ ] Batch insert optimization for initial backfill of existing events
- [ ] Incremental indexing for new events with checkpointing
- [ ] `SemanticSearchService` that wraps the query pattern (embed query → search DuckDB → return results)
- [ ] Configuration for brute-force vs. HNSW mode
- [ ] Periodic HNSW index rebuild for deployments using VSS

**Model Management**

- [ ] Model registry with version pinning (don't silently get a different model if HuggingFace updates)
- [ ] Support for quantized models (INT8/UINT8 ONNX) for reduced disk/memory
- [ ] Model preloading at startup with configurable timeout
- [ ] CLI tool or API to pre-download models (for air-gapped deployments)
- [ ] Cache eviction policy for old/unused model versions

**Configuration**

- [ ] `appsettings.json` / environment variable configuration for model selection, cache path, concurrency limits
- [ ] Configuration validation at startup (fail fast on invalid model/tokenizer combinations)

#### P2 — Nice to Have

**Performance**

- [ ] SIMD-optimized L2 normalization using `System.Numerics.Vector<T>`
- [ ] Memory-mapped model loading to reduce startup memory spike
- [ ] CoreML execution provider auto-detection on macOS (Apple Silicon Neural Engine)
- [ ] Benchmark suite comparing all models on representative KurrentDB event payloads

**Advanced Features**

- [ ] Matryoshka dimension truncation for models that support it (future: e5-v2, Qwen3)
- [ ] Instruction-prefixed embeddings for models that benefit from it (BGE, E5)
- [ ] Hybrid search combining BM25 full-text + semantic similarity scores
- [ ] Cross-encoder re-ranking for top-k refinement
- [ ] Embedding caching layer (LRU cache keyed by content hash) to avoid re-embedding unchanged events

**Ecosystem**

- [ ] NuGet package: `Kurrent.Extensions.AI.Embeddings`
- [ ] Documentation with model selection guide, deployment sizing, and migration paths
- [ ] Sample project: semantic search over KurrentDB event streams end-to-end
- [ ] gRPC service wrapper for out-of-process embedding (sidecar pattern)

## Security Considerations

- **Model provenance.** Models are downloaded from HuggingFace Hub over HTTPS. Consider pinning to specific commit SHAs rather than `resolve/main` for reproducibility and supply-chain safety.
- **Cache directory permissions.** Default cache (`~/.kurrent/models`) should be readable/writable only by the KurrentDB process user.
- **Input sanitization.** Tokenizers handle arbitrary UTF-8 input, but extremely long inputs could cause memory pressure. MaxSequenceLength truncation mitigates this.
- **No code execution.** ONNX models are computation graphs, not executable code. ONNX Runtime sandboxes execution to tensor operations.

## Alternatives Considered

| Alternative | Why Not |
|---|---|
| External API (OpenAI, Cohere) | Adds latency, cost, and external dependency. Conflicts with self-contained database philosophy. |
| Ollama/vLLM sidecar | Good for large models but adds operational complexity. Supported via standard `IEmbeddingGenerator` for users who want it. |
| GGML/llama.cpp | Better for decoder models but no native .NET bindings. ONNX Runtime has first-class .NET support. |
| TorchSharp | Full PyTorch in .NET but massive dependency footprint (~500MB+). Overkill for inference-only use case. |
| Custom C implementation | Maximum performance but unmaintainable. ONNX Runtime already has optimized CPU kernels. |

## File Inventory

```
Kurrent.Extensions.AI.Embeddings/
├── OnnxEmbeddingGenerator.cs              # Core IEmbeddingGenerator implementation
├── OnnxEmbeddingGenerator.WellKnown.cs    # Factory methods (CreateDefaultAsync, etc.)
├── OnnxEmbeddingGeneratorOptions.cs       # Base options with required properties
├── OnnxEmbeddingGeneratorExtensions.cs    # DI registration
├── PoolingStrategy.cs                     # Mean, CLS, LastToken enum
├── TextTokenizer.cs                       # Delegate + TokenizedInput record
├── Tokenizers.cs                          # Factory: Bert(), SentencePiece()
├── OnnxTensorNames.cs                     # Configurable tensor names
├── HuggingFaceModelResolver.cs            # Auto-download with caching
└── Models/
    ├── ModelOptions.cs                    # 11 strongly-typed option classes
    └── WellKnownModels.cs                # 11 model descriptors (HuggingFace paths)
```

## Open Questions

1. **Should embedding generation be synchronous or async?** ONNX Runtime's `Run()` is synchronous and CPU-bound. Currently wrapped in `Task.Run()` for the async `IEmbeddingGenerator` contract. Should we offer a synchronous path for hot-path scenarios?

2. **Model bundling vs. download.** Should the default KurrentDB distribution bundle bge-small-en-v1.5 (130MB) to avoid first-run downloads? Or keep it download-on-demand to minimize installer size?

3. **Embedding versioning.** If we change the default model in a future release, existing DuckDB embeddings become incompatible. Do we need a model version tag stored alongside embeddings? Migration tooling to re-embed?

4. **DuckDB VSS dependency.** Should KurrentDB take a hard dependency on the VSS extension, or should HNSW indexing remain entirely opt-in with documentation only?

5. **Quantized models.** INT8 ONNX models are 50-75% smaller with ~98% quality retention. Should we default to quantized variants for embedded deployments?

## References

- [Microsoft.Extensions.AI documentation](https://learn.microsoft.com/en-us/dotnet/ai/ai-extensions)
- [ONNX Runtime .NET API](https://onnxruntime.ai/docs/api/csharp-api)
- [DuckDB VSS Extension](https://duckdb.org/docs/stable/core_extensions/vss)
- [MTEB Leaderboard](https://huggingface.co/spaces/mteb/leaderboard)
- [sentence-transformers documentation](https://www.sbert.net/)
- [BGE-M3 paper](https://arxiv.org/abs/2402.03216)
- [Qwen3-Embedding](https://github.com/QwenLM/Qwen3-Embedding)
