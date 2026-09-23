#!/bin/bash
# SessionStart hook: links shared living state into worktree sessions.
# Shared singular stores (knowledge base, memory, skills, agents, rules) must have
# ONE physical home; a copy forks them. Fail-soft: a hook must never break a session.
set -uo pipefail

command -v jq >/dev/null 2>&1 || exit 0
cwd=$(jq -r '.cwd // empty' 2>/dev/null) || exit 0
[[ "$cwd" == */.claude/worktrees/* ]] || exit 0

main="${cwd%%/.claude/worktrees/*}"
rest="${cwd#*/.claude/worktrees/}"
wt="$main/.claude/worktrees/${rest%%/*}"

link() {
    local src="$main/$1" dst="$wt/$1"
    [[ -e "$src" ]] || return 0
    if [[ -L "$dst" ]]; then ln -sfn "$src" "$dst" 2>/dev/null || true; return 0; fi
    # A real file/dir here is a frozen creation-time copy: move aside, never delete.
    [[ -e "$dst" ]] && { mv "$dst" "$dst.snapshot.bak" 2>/dev/null || return 0; }
    mkdir -p "$(dirname "$dst")" 2>/dev/null || true
    ln -sfn "$src" "$dst" 2>/dev/null || true
}

link .claude/context
link .claude/memory
link .claude/skills
link .claude/agents
link .claude/rules
link .claude/ste100.json
link .agents

exit 0
