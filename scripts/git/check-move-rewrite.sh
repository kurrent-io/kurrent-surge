#!/usr/bin/env bash
# Blocks a commit that both deletes a file and creates one with the same basename.
#
# Git does not record moves — it infers them at read time by comparing content, and only links a
# delete to a create when they are similar enough (50% by default). Rewriting a file in the same
# commit that moves it pushes the pair under that threshold, so `git log --follow` on the new path
# dead-ends and the file's history is unreachable from where it now lives. The commit looks fine;
# the loss shows up later, when someone asks why a line is the way it is.
#
# The fix is always the same shape: split it. Rewrite in place first (same path, history continuous
# by definition), then `git mv` with content byte-identical (100% similarity, rename guaranteed).
#
# Reads the Claude Code PreToolUse payload on stdin, writes a permission decision on stdout.
set -uo pipefail

payload=$(cat)
command=$(printf '%s' "$payload" | jq -r '.tool_input.command // ""')

allow() { printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"allow"}}\n'; exit 0; }

# The `if` filter already narrows to git commit, but the hook must stay correct when invoked
# directly, and a compound command may carry a commit in any position.
[[ $command == *"git commit"* ]] || allow

# Rename detection left ON deliberately: a pair git already resolves as a rename (status R) is the
# safe case and never reaches the D/A lists below. What survives as delete + create is precisely the
# pair git could NOT link — the one whose history would be lost.
staged=$(git diff --cached --name-status 2>/dev/null) || allow
[[ -n $staged ]] || allow

deleted=$(awk '$1 == "D" { print $2 }' <<<"$staged")
added=$(awk '$1 == "A" { print $2 }' <<<"$staged")
[[ -n $deleted && -n $added ]] || allow

# Grouped by basename rather than listed as pairs: when several files share a name (Exceptions.cs in
# three modules) the pairing is ambiguous, and a cross-product of every delete against every create
# reads as noise.
collisions=""
while IFS= read -r name; do
    [[ -n $name ]] || continue
    collisions+="  ${name}"$'\n'
    collisions+="$(grep -x ".*/${name}\|${name}" <<<"$deleted" | sed 's/^/    deleted: /')"$'\n'
    collisions+="$(grep -x ".*/${name}\|${name}" <<<"$added" | sed 's/^/    created: /')"$'\n'
done < <(
    comm -12 \
        <(while IFS= read -r p; do [[ -n $p ]] && basename "$p"; done <<<"$deleted" | sort -u) \
        <(while IFS= read -r p; do [[ -n $p ]] && basename "$p"; done <<<"$added" | sort -u)
)

[[ -n $collisions ]] || allow

reason="This commit deletes a file and creates one with the same name, which is how file history gets lost.

${collisions}
Git infers moves by content similarity at read time. A commit that moves and rewrites a file in one
step usually falls under the detection threshold, so \`git log --follow\` on the new path stops at
this commit and everything before it becomes unreachable from where the file now lives.

Split it into two commits:
  1. Rewrite in place, at the original path. Same path, so history is continuous.
  2. \`git mv\` with the content byte-identical. 100% similarity, so the rename is always detected.

Confirm before committing with:
  git diff --cached -M --summary | grep -E 'rename|delete|create'

If the collision is a coincidence (unrelated files that happen to share a name), say so and re-run."

jq -n --arg reason "$reason" '{
    hookSpecificOutput: {
        hookEventName: "PreToolUse",
        permissionDecision: "deny",
        permissionDecisionReason: $reason
    }
}'
