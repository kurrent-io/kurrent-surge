#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARTIFACTS_DIR="$REPO_ROOT/.artifacts/benchmarks"
PROJECT="$REPO_ROOT/perf/Kurrent.Surge.Benchmarks/Kurrent.Surge.Benchmarks.csproj"

mkdir -p "$ARTIFACTS_DIR"

echo "Artifacts → $ARTIFACTS_DIR"
echo ""

dotnet run --project "$PROJECT" -c Release --framework net10.0 -- \
  --artifacts "$ARTIFACTS_DIR" \
  --exporters GitHub JSON \
  --disableLogFile \
  "$@"
