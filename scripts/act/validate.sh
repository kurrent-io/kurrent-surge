#!/usr/bin/env bash
# Dry run — validate workflows without executing.
source "$(dirname "$0")/_base.sh"
run_act -n "$@"
