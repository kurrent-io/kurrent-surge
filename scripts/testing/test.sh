#!/usr/bin/env bash
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"
RUN_ID="$(date +%Y%m%d-%H%M%S)-$(head -c 2 /dev/urandom | xxd -p)"

# --- template -----------------------------------------------------------------
# The canonical way to run tests in this solution. Only two values change:
#   {results_dir}  — unique per run
#   {filter}       — treenode filter with category always enforced
#
# Everything after the template is extra args passed through verbatim.
# Full output goes to {results_dir}/output.log; terminal gets failures + summary.

run() {
  local results_dir="$1" filter="$2"
  shift 2

  mkdir -p "$results_dir"
  local logfile="$results_dir/output.log"

  dotnet test -c Release \
    --no-progress \
    --ignore-exit-code 8 \
    --results-directory "$results_dir" \
    -- \
    --disable-logo \
    --treenode-filter "$filter" \
    --coverage \
    --coverage-output "report.cobertura" \
    --report-trx \
    --report-trx-filename "report.trx" \
    --report-html-filename "$results_dir/report.html" \
    --log-level Trace \
    --detailed-stacktrace \
    "$@" \
    2>&1 | tee "$logfile" | awk '
      /^failed /        { fail=1 }
      fail && /^[^ ]/   { if (!/^failed /) fail=0 }
      fail              { print; next }
      /^skipped /       { print; next }
      /^Test run summary:/ { summary=1 }
      summary           { print }
    '
}

# --- help ---------------------------------------------------------------------

show_help() {
  cat <<'EOF'
test.sh — canonical test runner for this solution

USAGE
  test.sh <action> <category> [tunit-args...]
  test.sh help

ACTIONS
  run     Execute tests and produce results
  list    Discover and list matching tests without running them

CATEGORIES
  unit          Exclude tests with [Category("Integration")]
  integration   Include only [Category("Integration")] tests

  By convention, all integration tests MUST be decorated with [Category("Integration")].
  Tests without this attribute are treated as unit tests. If an integration test appears
  in unit runs or is missing from integration runs, it is missing the category attribute.

EXAMPLES
  test.sh run unit                                          # all unit tests
  test.sh run integration                                   # all integration tests
  test.sh run unit --treenode-filter "/*/*/MyTests/*"        # specific class
  test.sh run integration --timeout 30s --fail-fast          # extra TUnit args
  test.sh list unit                                         # list all unit tests
  test.sh list unit --treenode-filter "/*/*/MyTests/*"       # list matching tests

FILTER MERGING
  The category filter is always enforced. If you pass --treenode-filter, the
  category property is appended to the last segment of your filter:

    input:  /*/*/MyTests/*                 → /*/*/MyTests/*[Category!=Integration]
    input:  /*/*/*/*[Priority=High]        → /*/*/*/*[(Priority=High)&(Category!=Integration)]

  TUnit filter docs: https://tunit.dev/docs/execution/test-filters/

RESULTS
  Each run creates a unique folder under .artifacts/test-results/<run-id>/:

    report.trx       Test results — always use this to check pass/fail status
    report.html      Interactive HTML report — open in the browser for a visual overview
    report.cobertura Code coverage binary — requires a coverage tool to read
    output.log       Raw dotnet test output — only for debugging the command itself

  The TRX and HTML files contain merged results from all test projects.
  Always use report.trx to check test outcomes. Do NOT parse output.log for results.

EXIT CODES
  0   All tests passed
  2   One or more tests failed
  8   Zero tests matched the filter (ignored by --ignore-exit-code 8)
EOF
  exit 0
}

# --- parse action + category --------------------------------------------------

ACTION="${1:-help}"
shift 2>/dev/null || true

case "$ACTION" in
  help|--help|-h) show_help ;;
  run|list) ;;
  *)
    echo "Unknown action: $ACTION" >&2
    echo "Usage: test.sh <run|list> <unit|integration> [tunit-args...]" >&2
    exit 1
    ;;
esac

CATEGORY="${1:-}"
shift 2>/dev/null || true

case "$CATEGORY" in
  unit)        CATEGORY_PROP="Category!=Integration" ;;
  integration) CATEGORY_PROP="Category=Integration" ;;
  *)
    echo "Unknown category: ${CATEGORY:-<missing>}" >&2
    echo "Usage: test.sh $ACTION <unit|integration> [tunit-args...]" >&2
    exit 1
    ;;
esac

# --- build filter -------------------------------------------------------------

# Extract --treenode-filter from user args so we can merge the category into it.
# TUnit only accepts one --treenode-filter value.
PASSTHROUGH=()
USER_FILTER=""
while [[ $# -gt 0 ]]; do
  if [[ "$1" == "--treenode-filter" ]]; then
    USER_FILTER="$2"; shift 2
  else
    PASSTHROUGH+=("$1"); shift
  fi
done

# Append category property to the filter's last segment.
# If the filter already has properties [X=Y], merge with AND: [(X=Y)&(Category...)].
if [[ -n "$USER_FILTER" ]]; then
  if [[ "$USER_FILTER" == *"]" ]]; then
    path="${USER_FILTER%%\[*}"
    props="${USER_FILTER#*\[}" && props="${props%]}"
    FILTER="${path}[(${props})&(${CATEGORY_PROP})]"
  else
    FILTER="${USER_FILTER}[${CATEGORY_PROP}]"
  fi
else
  FILTER="/*/*/*/*[${CATEGORY_PROP}]"
fi

# --- list (discovery only) ----------------------------------------------------

if [[ "$ACTION" == "list" ]]; then
  echo "━━ list $CATEGORY tests ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
  echo "  Filter:    $FILTER"
  echo ""
  dotnet test \
    --list-tests \
    --no-progress \
    --ignore-exit-code 8 \
    -- \
    --disable-logo \
    --treenode-filter "$FILTER" \
    2>&1 | grep -v '^Discovering\|^$'
  exit $?
fi

# --- run ----------------------------------------------------------------------

RESULTS_DIR="$REPO_ROOT/.artifacts/test-results/$RUN_ID"

LABEL="$CATEGORY tests"
LINE_RUN="  Run ID:    $RUN_ID"
LINE_FILTER="  Filter:    $FILTER"
LINE_RESULTS="  Results:   .artifacts/test-results/$RUN_ID"
WIDTH=${#LINE_RUN}
(( ${#LINE_FILTER} > WIDTH )) && WIDTH=${#LINE_FILTER}
(( ${#LINE_RESULTS} > WIDTH )) && WIDTH=${#LINE_RESULTS}
(( WIDTH += 2 ))
(( WIDTH < 80 )) && WIDTH=80
RULE=$(printf '━%.0s' $(seq 1 "$WIDTH"))
echo "━━ $LABEL ${RULE:$(( ${#LABEL} + 4 ))}"
echo "$LINE_RUN"
echo "$LINE_FILTER"
echo "$LINE_RESULTS"
echo "$RULE"
echo ""

run "$RESULTS_DIR" "$FILTER" "${PASSTHROUGH[@]}"
EXIT_CODE=$?

REL_RESULTS=".artifacts/test-results/$RUN_ID"

EXTRA=""
if [[ ${#PASSTHROUGH[@]} -gt 0 ]]; then
  EXTRA=" \\
    ${PASSTHROUGH[*]}"
fi

CMD_LINES=(
  "  dotnet test \\"
  "    -c Release --no-progress --ignore-exit-code 8 \\"
  "    --results-directory $REL_RESULTS \\"
  "    -- \\"
  "    --disable-logo --log-level Trace --detailed-stacktrace \\"
  "    --treenode-filter \"$FILTER\" \\"
  "    --coverage --coverage-output \"report.cobertura\" \\"
  "    --report-trx --report-trx-filename \"report.trx\" \\"
  "    --report-html-filename \"$REL_RESULTS/report.html\"${EXTRA}"
)
CMD_WIDTH=0
for line in "${CMD_LINES[@]}"; do
  (( ${#line} > CMD_WIDTH )) && CMD_WIDTH=${#line}
done
(( CMD_WIDTH += 2 ))
CMD_LABEL="command"
CMD_RULE=$(printf '━%.0s' $(seq 1 "$CMD_WIDTH"))

echo ""
echo "━━ $CMD_LABEL ${CMD_RULE:$(( ${#CMD_LABEL} + 4 ))}"
for line in "${CMD_LINES[@]}"; do echo "$line"; done
echo "$CMD_RULE"

exit $EXIT_CODE
