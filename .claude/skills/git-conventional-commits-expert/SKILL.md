---
name: git-conventional-commits-expert
description: Use when creating git commits, writing commit messages, or reviewing commit history. Ensures commits follow Conventional Commits v1.0.0 spec for automated changelogs and semantic versioning. Triggers on "commit this", "write commit message", "git commit", "commit changes", "review commits", "fix commit message".
license: CC0-1.0 (Public Domain)
---

# Git Conventional Commits Expert

Expert guidance for creating, analyzing, and validating commit messages that follow the Conventional Commits v1.0.0 specification.

## Purpose

This skill transforms you into an expert on the Conventional Commits specification, enabling precise guidance on creating human and machine-readable commit messages that provide explicit commit history across projects. The specification enables automated tooling for changelog generation, semantic versioning, and commit history exploration.

## When to Use This Skill

Use this skill when users:

- Need help writing a commit message
- Ask about commit message conventions or best practices
- Want to validate or analyze existing commit messages
- Request explanations of commit types, scopes, or breaking changes
- Need guidance on setting up commit standards for a project
- Ask about semantic versioning in relation to commits
- Want to understand how to structure commits for automated tools

## Core Concepts

### Commit Message Structure

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

### Primary Commit Types

**feat:** Introduces a new feature (correlates with MINOR in SemVer)
**fix:** Patches a bug (correlates with PATCH in SemVer)

### Common Additional Types

While not mandated by the specification, these types are widely adopted:

- **build:** Changes to build system or dependencies
- **chore:** Routine tasks, maintenance (no production code change)
- **ci:** Changes to CI configuration files and scripts
- **docs:** Documentation changes only
- **perf:** Performance improvements
- **refactor:** Code changes that neither fix bugs nor add features
- **revert:** Reverts a previous commit
- **style:** Code style changes (formatting, missing semicolons, etc.)
- **test:** Adding or modifying tests

### Breaking Changes

Breaking changes can be indicated in two ways:

1. **Append `!` after type/scope:** `feat!:` or `feat(api)!:`
2. **Footer with `BREAKING CHANGE:`** followed by description

Breaking changes correlate with MAJOR version bumps in SemVer.

### Scope

Optional contextual information in parentheses after the type:
- `feat(parser):` - Changes to the parser component
- `fix(auth):` - Bug fix in authentication
- `docs(readme):` - README documentation update

## How to Use This Skill

### Crafting Commit Messages

When helping users write commits, follow this process:

1. **Understand the change:** Ask about what was modified, added, or fixed
2. **Determine the type:** Select the appropriate commit type based on the nature of the change
3. **Identify the scope (if applicable):** Suggest a scope if the change is isolated to a specific component
4. **Write the description:** Create a concise, imperative mood description (e.g., "add feature" not "added feature")
5. **Check for breaking changes:** If the change breaks backward compatibility, include `!` or a BREAKING CHANGE footer
6. **Add body if needed:** For complex changes, include additional context in the body
7. **Add footers if appropriate:** Include references, reviewers, or breaking change descriptions

### Validating Existing Commits

When analyzing commits, check for:

1. **Correct format:** Type, optional scope in parentheses, colon, space, description
2. **Valid type:** Must be a recognized type (at minimum: feat, fix)
3. **Proper description:** Lowercase, imperative mood, no period at end
4. **Breaking change indicators:** Consistent use of `!` or BREAKING CHANGE footer
5. **Body and footer formatting:** Blank lines separating sections, proper footer format

### Providing Recommendations

When suggesting improvements:

- Explain the rationale behind each recommendation
- Reference the specific specification rule when applicable
- Provide before/after examples
- Explain how the commit relates to semantic versioning
- Consider the team's existing conventions while staying specification-compliant

### Example Interactions

**User asks: "How do I write a commit for adding a new authentication feature?"**

Response approach:
1. Identify this as a new feature (use `feat` type)
2. Suggest appropriate scope: `feat(auth):`
3. Provide complete examples with and without body
4. Explain SemVer impact (MINOR bump)

**User asks: "Is this commit message correct: 'Fixed the bug in parser'"**

Response approach:
1. Analyze the message structure
2. Identify issues (missing type prefix, capitalization, period)
3. Provide corrected version: `fix(parser): resolve parsing error`
4. Explain the corrections made

**User asks: "We're removing support for Node 12, how should I commit this?"**

Response approach:
1. Identify this as a breaking change
2. Show both `!` notation and footer approaches
3. Provide examples:
   - `feat!: drop Node 12 support`
   - Or with footer: `BREAKING CHANGE: Node 12 is no longer supported`
4. Explain SemVer impact (MAJOR bump)

## Reference Materials

For detailed specification rules, consult `references/specification.md` which contains the complete Conventional Commits v1.0.0 specification including:

- Complete specification rules (RFC 2119 keywords)
- All structural requirements
- Comprehensive examples
- FAQ section
- Rationale for using conventional commits

Load this reference when:
- Users need detailed specification clarification
- Edge cases require specification verification
- Complete examples are needed for complex scenarios
- Users want to understand the "why" behind the specification

## Best Practices

### Writing Effective Descriptions

- Use imperative mood: "add", "fix", "update" (not "added", "fixed", "updated")
- Start with lowercase
- No period at the end
- Be specific but concise
- Limit to 50-72 characters when possible

### When to Use Body

Include a body when:
- The change needs additional context
- The "why" is as important as the "what"
- Multiple related changes were made
- Implementation details help future understanding

### When to Use Footers

Common footer use cases:
- `BREAKING CHANGE:` - Describe breaking changes
- `Refs:` or `Closes:` - Reference issues/tickets
- `Reviewed-by:` - Credit reviewers
- `Co-authored-by:` - Acknowledge contributors

### Scope Guidelines

Scopes should be:
- Nouns describing a section of the codebase
- Consistent across the project
- Documented in the project's contribution guide
- Optional but recommended for larger projects

## Semantic Versioning Relationships

- **MAJOR (X.0.0):** Commits with BREAKING CHANGE (any type)
- **MINOR (0.X.0):** Commits with type `feat`
- **PATCH (0.0.X):** Commits with type `fix`
- **No version change:** Other types (docs, chore, etc.)

## Common Pitfalls to Avoid

1. **Mixing changes:** Don't combine features and fixes in one commit
2. **Vague descriptions:** "fix stuff" or "update code" are not helpful
3. **Past tense:** "added feature" should be "add feature"
4. **Missing type:** All commits must have a type prefix
5. **Inconsistent scopes:** Use established scope names
6. **Forgetting breaking changes:** Always mark API-breaking changes
7. **Excessive scope:** Commits should be atomic and focused

## Communication Style

When using this skill:

- Be clear and prescriptive with commit message recommendations
- Provide specific examples rather than general advice
- Explain the reasoning behind recommendations
- Reference the specification when clarifying rules
- Adapt tone to user's expertise level
- Offer alternatives when multiple valid approaches exist
- Validate user's understanding by providing confirmation examples

## Quality Standards

Strive for commit messages that are:

- **Descriptive:** Clear about what changed
- **Concise:** No unnecessary words
- **Consistent:** Following the same patterns
- **Informative:** Useful for future readers
- **Actionable:** Enabling automated tooling
- **Compliant:** Adhering to the specification
