#!/usr/bin/env bash
# Shared base for all act wrapper scripts.
# Source this, then call: run_act [extra-args...]
set -euo pipefail

missing=()
[[ -z "${ORG_GH_BOT_AUTOMATION:-}" ]] && missing+=(ORG_GH_BOT_AUTOMATION)
[[ -z "${ORG_GH_BOT_USERNAME:-}" ]] && missing+=(ORG_GH_BOT_USERNAME)

if [[ ${#missing[@]} -gt 0 ]]; then
  echo "Missing required environment variables: ${missing[*]}" >&2
  echo "Add them to your ~/.zshrc, e.g.:" >&2
  for var in "${missing[@]}"; do
    echo "  export $var=\"...\"" >&2
  done
  exit 1
fi

# Auto-detect Docker socket from current context
export DOCKER_HOST="${DOCKER_HOST:-$(docker context inspect --format '{{.Endpoints.docker.Host}}' 2>/dev/null || echo "unix:///var/run/docker.sock")}"

# Env vars whose names contain these keywords are treated as secrets (masked in logs).
# Everything else goes as vars (visible). Case-insensitive.
# - Substring match: CREDENTIAL, PASSWORD, SECRET (always sensitive regardless of position)
# - Word-boundary match: TOKEN, KEY, PAT, AUTOMATION (avoid PATH, FPATH, MANPATH, etc.)
SECRET_PATTERN="CREDENTIAL|PASSWORD|SECRET|(^|_)(TOKEN|KEY|PAT|AUTOMATION)(_|=)"

run_act() {
  local secret_count var_count
  secret_count=$(env | grep -ciE "^[^=]*(${SECRET_PATTERN})" || true)
  var_count=$(env | grep -cviE "^[^=]*(${SECRET_PATTERN})" || true)

  echo "🔒 Secrets (${secret_count}, masked in logs):"
  env | grep -iE "^[^=]*(${SECRET_PATTERN})" | cut -d= -f1 | sort | while IFS= read -r name; do
    echo "   $name"
  done

  echo ""
  echo "🔓 Variables: ${var_count} (passed as-is)"
  echo ""

  exec act \
    --container-architecture linux/amd64 \
    -P ubuntu-latest=catthehacker/ubuntu:act-latest \
    --secret-file <(env | grep -iE "^[^=]*(${SECRET_PATTERN})") \
    --var-file <(env | grep -viE "^[^=]*(${SECRET_PATTERN})") \
    "$@"
}
