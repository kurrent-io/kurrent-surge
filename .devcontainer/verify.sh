#!/bin/bash
# verify.sh — Quick smoke test for the devcontainer.
set -euo pipefail

PASS=0; FAIL=0

ok()   { echo "  ✅ $1"; PASS=$((PASS + 1)); }
fail() { echo "  ❌ $1"; FAIL=$((FAIL + 1)); }

cmd() {
    local label="$1" bin="$2"
    if v=$("$bin" --version 2>&1 | head -1); then
        ok "$label — $v"
    else
        fail "$label — not found"
    fi
}

env() {
    local var="$1"
    [ -n "${!var:-}" ] && ok "$var" || fail "$var (not set)"
}

echo "🧪 Devcontainer Verification"
echo ""

echo "── Runtime ──"
cmd "dotnet"  dotnet
cmd "node"    node
cmd "python3" python3
cmd "bun"     bun

echo ""
echo "── Tools ──"
cmd "git"     git
cmd "gh"      gh
cmd "docker"  docker
cmd "duckdb"  duckdb
cmd "zsh"     zsh

echo ""
echo "── .NET SDK ──"
dotnet --list-sdks 2>/dev/null | while read -r line; do ok "SDK $line"; done
dotnet --list-runtimes 2>/dev/null | while read -r line; do ok "$line"; done

echo ""
echo "── Environment ──"
env DOTNET_CLI_TELEMETRY_OPTOUT
env DOTNET_NOLOGO
env TESTINGPLATFORM_TELEMETRY_OPTOUT

echo ""
echo "── User ──"
[ "$(whoami)" != "root" ] && ok "non-root: $(whoami)" || fail "running as root"

echo ""
if [ "$FAIL" -eq 0 ]; then
    echo "✅ All $PASS checks passed"
else
    echo "❌ $FAIL failed, $PASS passed"
    exit 1
fi
