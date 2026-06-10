#!/usr/bin/env bash
# Run the CI job (pull request workflow).
source "$(dirname "$0")/_base.sh"
run_act pull_request -j ci -W .github/workflows/pull-request.yml "$@"
