#!/usr/bin/env bash
# Run the build docs workflow.
source "$(dirname "$0")/_base.sh"
run_act push -j dispatch -W .github/workflows/build-docs.yml "$@"
