#!/usr/bin/env python3
"""Configure the development environment for Claude Code cloud sessions.

Called by the SessionStart hook in .claude/settings.json.
Can also be pasted into the cloud environment's "Setup script" field
for one-time SDK installation (runs before Claude Code launches).

Idempotent: safe to run on every session start (resumed or new).
Each section is independent — a failure in one won't block the others.
"""

import json
import os
import subprocess
import sys
from pathlib import Path

def log(msg: str) -> None:
    print(f">> {msg}", file=sys.stderr, flush=True)

def run(*args: str, check: bool = True, **kwargs) -> subprocess.CompletedProcess:
    return subprocess.run(args, check=check, **kwargs)

# ── GitHub CLI ─────────────────────────────────────────────────────────

def install_gh() -> None:
    if _which("gh"):
        log("gh CLI already installed, skipping")
        return

    log("Installing gh CLI...")
    keyring = Path("/usr/share/keyrings/githubcli-archive-keyring.gpg")
    run(
        "curl", "-fsSL",
        "https://cli.github.com/packages/githubcli-archive-keyring.gpg",
        "-o", str(keyring),
    )

    arch = subprocess.check_output(["dpkg", "--print-architecture"], text=True).strip()
    sources = Path("/etc/apt/sources.list.d/github-cli.list")
    sources.write_text(
        f"deb [arch={arch} signed-by={keyring}] "
        "https://cli.github.com/packages stable main\n"
    )

    run("apt-get", "update", "-qq")
    run("apt-get", "install", "-y", "-qq", "gh", stdout=subprocess.DEVNULL)

# ── .NET SDKs ────────────────────────────────────────────────────────

def install_sdk(channel: str) -> None:
    dotnet = DOTNET_INSTALL_DIR / "dotnet"
    if dotnet.exists():
        result = run(str(dotnet), "--list-sdks", check=False, capture_output=True, text=True)
        for line in result.stdout.splitlines():
            if line.startswith(f"{channel}."):
                log(f".NET {channel} SDK already installed, skipping")
                return

    log(f"Installing .NET {channel} SDK...")
    run(
        "bash", "-c",
        f"curl -fsSL https://dot.net/v1/dotnet-install.sh | "
        f"bash -s -- --channel {channel} --install-dir {DOTNET_INSTALL_DIR}",
    )

# ── Persist environment ──────────────────────────────────────────────

def persist_env() -> None:
    env_file = os.environ.get("CLAUDE_ENV_FILE")
    if not env_file:
        return

    log("Persisting environment variables...")
    lines = [
        f"DOTNET_ROOT={DOTNET_INSTALL_DIR}",
        f"DOTNET_INSTALL_DIR={DOTNET_INSTALL_DIR}",
        f"PATH={DOTNET_INSTALL_DIR}:{DOTNET_INSTALL_DIR / 'tools'}:{os.environ['PATH']}",
    ]

    dot_env = SCRIPT_DIR.parent / ".env"
    if dot_env.is_file():
        for line in dot_env.read_text().splitlines():
            stripped = line.strip()
            if stripped and not stripped.startswith("#"):
                lines.append(stripped)

    with open(env_file, "a") as f:
        f.write("\n".join(lines) + "\n")

# ── Trust CA certificate (test TLS) ─────────────────────────────────

def trust_ca() -> None:
    ca_cert = PROJECT_DIR / "scripts" / "certs" / "ca" / "ca.crt"
    ca_dest = Path("/usr/local/share/ca-certificates/kurrent_ca.crt")

    if not ca_cert.is_file():
        return
    if ca_dest.is_file():
        log("CA certificate already trusted, skipping")
        return

    log("Trusting test CA certificate...")
    ca_dest.write_bytes(ca_cert.read_bytes())
    run("update-ca-certificates", check=False, stderr=subprocess.DEVNULL)

# ── Helpers ──────────────────────────────────────────────────────────

def _which(cmd: str) -> bool:
    return subprocess.run(["which", cmd], capture_output=True).returncode == 0

# ── Main ─────────────────────────────────────────────────────────────

DOTNET_INSTALL_DIR = Path(os.environ.get("DOTNET_INSTALL_DIR", Path.home() / ".dotnet"))
SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_DIR = Path(os.environ.get("CLAUDE_PROJECT_DIR", SCRIPT_DIR.parent.parent))

SECTIONS = [
    ("gh CLI", install_gh),
    (".NET 10.0 SDK", lambda: install_sdk("10.0")),
    ("env persistence", persist_env),
    ("CA certificate", trust_ca),
]

def main() -> None:
    if os.environ.get("CLAUDE_CODE_REMOTE") != "true":
        log("Not a remote session, skipping setup.")
        print(json.dumps({"systemMessage": "Local session — remote setup skipped."}))
        sys.exit(0)

    # Update PATH so downstream sections can find dotnet
    os.environ["PATH"] = (
        f"{DOTNET_INSTALL_DIR}:{DOTNET_INSTALL_DIR / 'tools'}:{os.environ['PATH']}"
    )
    os.environ["DOTNET_ROOT"] = str(DOTNET_INSTALL_DIR)

    errors = []
    for name, fn in SECTIONS:
        try:
            fn()
        except Exception as e:
            log(f"WARN: {name} failed: {e}")
            errors.append(name)

    if not errors:
        log("Remote environment setup complete.")
        msg = "Remote environment ready: gh CLI, .NET SDK, env vars, CA cert — all OK."
    else:
        log(f"Remote environment setup finished with errors: {', '.join(errors)}")
        msg = f"Remote environment partially ready. Failed: {', '.join(errors)}. Check stderr for details."

    print(json.dumps({"systemMessage": msg}))

if __name__ == "__main__":
    main()
