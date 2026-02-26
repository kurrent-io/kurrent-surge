---
name: markdown-style
description: |
  Markdown writing conventions. Invoke BEFORE writing or editing any .md file, plan, doc, PR description, commit
  message, report, or any markdown content. This includes skill files, templates, project docs, and scratch files.
---

## Prose

- Keep lines under 120 characters
- Never use em dashes, en dashes, or hyphens to join phrases. Use periods, semicolons, or restructure the sentence
  instead

TRIPWIRE: if you are capping lines way before the 120 character limit - because of your natural bias - reflow the text 
to use more of the line.

## Structure

- One blank line before and after headings
- One blank line before and after lists
- One blank line before and after code blocks
- No skipping heading levels (h1 -> h3). Always step incrementally
- No decorative separators (ASCII art, `---` between every section, banner comments)
- Use `##` as the top-level heading in most files. Reserve `#` for the document title only
- Add a TOC (`## Contents` with anchor links) when the file exceeds 100 lines

## Links

- When using markdown links the link text is normally the filename or a natural description, not the full path

## Lists

- Use `-` for unordered lists, not `*`
- Use `1.` for ordered lists only when sequence matters
- Keep list items parallel in grammar. If one starts with a verb, all should start with a verb

## Code blocks

- Always specify the language for fenced code blocks (```sh, ```csharp)
- Use inline backticks for code references within prose (`ClassName`, `methodName`)

## Tables

- Always align columns with padding so the pipes form straight vertical lines
- Keep tables simple. If a table has more than 5 columns, consider restructuring as a list or subsections
- Format tables with `dotnet .claude/skills/markdown-style/scripts/format-tables.cs <file>` or pipe content via stdin

TRIPWIRE: if you notice misaligned tables, run the formatter script instead of manually adjusting spaces

## What NOT to do

- Do not add "last updated" timestamps
- Do not use emoji unless the user explicitly asks for it
- Do not wrap prose in HTML tags
