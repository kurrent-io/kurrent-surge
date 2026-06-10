#!/usr/bin/env bash
# Run the benchmark workflow.
source "$(dirname "$0")/_base.sh"
run_act workflow_dispatch -j benchmark -W .github/workflows/benchmark.yml "$@"
