#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
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

# List-only invocations and dry runs leave no JSON — skip the report rather than fail on it.
if compgen -G "$ARTIFACTS_DIR/results/*-report-full*.json" > /dev/null; then
  echo ""
  dotnet run "$REPO_ROOT/scripts/benchmarks/benchmark-report.cs" -- \
    --input "$ARTIFACTS_DIR/results" \
    --output "$ARTIFACTS_DIR/report.html"
fi
