#!/usr/bin/env bash
# List all workflows and jobs.
source "$(dirname "$0")/_base.sh"
run_act -l "$@"
