# Project Context

This directory is the shared context layer for both AI coding agents and
human developers. Instead of scattering architecture notes, conventions,
and onboarding context across READMEs, wikis, and tribal knowledge, it
lives here in version-controlled markdown that stays close to the code.

## How it works

`project.md` is the entry point. It covers the stack, build commands, and
links to reference files for deeper topics (architecture, conventions,
specific modules). Agents read it before making changes; developers can
read it to get up to speed.

Reference files (`project-*.md`) go deeper on individual topics. You only
need to read the ones relevant to what you're working on.

## Generating and updating

These files are generated and maintained by the `/project-init` skill
(`.claude/skills/project-init/`). It scans the codebase, reads existing
docs and config, and produces compact, focused context files.

- **First run**: generates all files from templates and shows a summary
- **Subsequent runs**: proposes changes as a checklist for you to approve

Run `/project-init` after major structural changes to keep things current.
You can also edit the files directly; the skill will respect your changes
on its next review pass.

## Agent instructions

`instructions.md` contains behavioral instructions for AI agents. It is
the single source of truth; these root files are all symlinks to it:

- `/CLAUDE.md` → `.project/instructions.md`
- `/AGENTS.md` → `.project/instructions.md`
- `/GEMINI.md` → `.project/instructions.md`
- `/.junie/guidelines.md` → `.project/instructions.md`
- `/.github/copilot-instructions.md` → `.project/instructions.md`

Edit `instructions.md` once; every agent picks up the change.
