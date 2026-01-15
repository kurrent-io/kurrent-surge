#!/bin/bash
# Post-Compact Context Reload Hook
# Runs via SessionStart with "compact" matcher after compaction completes
# Injects critical context and instructions for Claude to re-establish project awareness

set -e

# Build the context injection message
read -r -d '' CONTEXT << 'CONTEXT_EOF' || true
<post-compaction-restoration>
Context compaction just completed. Your conversation history has been summarized.

<mandatory-actions>
1. RE-READ CLAUDE.md - Re-read and apply the project's CLAUDE.md file

2. RESTORE WORKING CONTEXT
   - Check TodoWrite for pending tasks and update status if needed
   - Review any plan files if you were in planning mode
   - Check git status if making code changes
   - Verify current task understanding before proceeding

3. ACKNOWLEDGE - Briefly confirm context restoration to the user
</mandatory-actions>

<preservation-priorities>
What was kept in summary:
- Key architectural decisions and patterns discussed
- User's explicit requests and requirements
- Your previous actions and their outcomes
- Critical file paths and code locations
- Any errors or issues being debugged
</preservation-priorities>

<context-warnings>
What may be lost:
- Verbatim code snippets (re-read files if needed)
- Detailed exploration paths
- Intermediate debugging steps

When uncertain, re-read relevant files rather than assuming.
</context-warnings>
</post-compaction-restoration>
CONTEXT_EOF

# Escape the context for JSON
ESCAPED_CONTEXT=$(echo "$CONTEXT" | jq -Rs .)

# Output the hook response with additionalContext
cat << EOF
{
  "hookSpecificOutput": {
    "hookEventName": "SessionStart",
    "additionalContext": $ESCAPED_CONTEXT
  }
}
EOF

exit 0
