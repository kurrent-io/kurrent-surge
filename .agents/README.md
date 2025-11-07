# AI Agent Instructions Directory

## Purpose

This directory contains centralized instruction files for AI coding agents working with the KurrentDB codebase.

## File Structure

```
.agents/
├── README.md                # This file
├── instructions.md          # Canonical source for all agent instructions
└── emoji.instructions.md    # Emoji taxonomy standards
```

## Canonical Source

**`instructions.md`** is the **single source of truth** for agent instructions. All other agent instruction files in the repository are symbolic links to this file:

- `/AGENTS.md` → `.agents/instructions.md`
- `/CLAUDE.md` → `.agents/instructions.md`
- `/.junie/guidelines.md` → `.agents/instructions.md`
- `/.github/copilot-instructions.md` → `.agents/prompt.md` (separate file)

## Why This Structure?

**Single Maintenance Point**: Update instructions once in `.agents/instructions.md`, and all agents (Claude Code, GitHub Copilot, Junie, etc.) automatically receive the same guidance.

**Consistency**: Ensures all AI agents follow identical conventions, preventing conflicting instructions across different tools.

**Discoverability**: All agent instruction symlinks resolve to `.agents/instructions.md`, making it easy to find and update agent guidelines.

## Editing Instructions

To modify agent instructions:

1. Edit `.agents/instructions.md`
2. Changes automatically propagate to all symlinked locations
3. No need to update multiple files

## Related Documentation

- **`.project/project.md`** - Comprehensive AI agent guidance (architecture, patterns, workflows)
- **`.project/reports/`** - Pattern validation and discrepancy reports

---

*Maintained by: Engineering Team*
*Last Updated: 2025-11-05*
