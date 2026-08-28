#!/usr/bin/env bash
# Runs the docs site locally with hot reload. Installs dependencies on first use.
#
# Bun-everywhere: no npm, no npx, no pnpm. The `--bun` flag is load-bearing —
# astro's bin carries a `#!/usr/bin/env node` shebang, so plain `bunx astro`
# hands execution to Node. `--bun` overrides the shebang and keeps the Bun
# runtime, which is what the policy asks for.
set -euo pipefail

cd "$(dirname "$0")"

if ! command -v bun >/dev/null 2>&1; then
  echo "bun is required but not installed. See https://bun.com/docs/installation" >&2
  exit 1
fi

[ -d node_modules ] || bun install

exec bunx --bun astro dev --host --port "${PORT:-4321}"
