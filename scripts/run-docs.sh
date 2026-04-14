#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOCS_DIR="$REPO_ROOT/docs"
REQUIRED_NODE=20

# --- node ---

if ! command -v node &>/dev/null; then
  echo "error: node is not installed"
  echo "install node $REQUIRED_NODE+ via homebrew, fnm, or nvm"
  exit 1
fi

NODE_MAJOR=$(node -v | sed 's/v\([0-9]*\).*/\1/')
if (( NODE_MAJOR < REQUIRED_NODE )); then
  echo "error: node $REQUIRED_NODE+ required (found $(node -v))"
  echo "install or switch to node $REQUIRED_NODE+"
  exit 1
fi

# --- pnpm ---

if ! command -v pnpm &>/dev/null; then
  echo "pnpm not found, installing via corepack..."
  corepack enable
  corepack prepare pnpm@9.10.0 --activate
fi

# --- dependencies ---

if [ ! -d "$DOCS_DIR/node_modules" ]; then
  echo "installing dependencies..."
  (cd "$DOCS_DIR" && pnpm install)
elif [ "$DOCS_DIR/package.json" -nt "$DOCS_DIR/node_modules/.pnpm/lock.yaml" ] || \
     [ "$DOCS_DIR/pnpm-lock.yaml" -nt "$DOCS_DIR/node_modules/.pnpm/lock.yaml" ]; then
  echo "dependencies outdated, updating..."
  (cd "$DOCS_DIR" && pnpm install)
elif [ ! -e "$DOCS_DIR/node_modules/vuepress/bin/vuepress-vite.js" ]; then
  echo "dependencies corrupted, reinstalling..."
  (cd "$DOCS_DIR" && pnpm install)
fi

# --- run ---

echo "starting docs dev server..."
cd "$DOCS_DIR"
exec pnpm dev "$@"
