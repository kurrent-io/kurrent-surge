#!/usr/bin/env bash
# Run the main branch build-and-test workflow.
source "$(dirname "$0")/_base.sh"
run_act -j main -W .github/workflows/main.yml "$@"
