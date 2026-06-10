#!/usr/bin/env bash
# Run the release workflow.
source "$(dirname "$0")/_base.sh"
run_act workflow_dispatch -j release -W .github/workflows/release.yml "$@"
