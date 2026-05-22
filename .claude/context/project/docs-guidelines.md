# Documentation Guidelines

## Keep Docs in Sync with Code

Check if documentation updates are needed when:

- New features or public APIs are added
- API endpoints, methods, or interfaces change
- Breaking changes are introduced
- Dependencies or requirements change
- Configuration options or environment variables are modified
- Installation or setup procedures change
- CLI commands or scripts are updated
- Code examples in documentation become outdated

### Rules

- Update documentation in the same commit as the code change when possible.
- When modifying public API surface, check if `docs/` pages need updating.
- Include before/after examples for breaking changes.
- Test code examples before committing — outdated examples are worse than no examples.
- Keep documentation DRY: link instead of duplicating content across files.
- Do not document features that don't exist yet.

## Never Leak Real Data into Documentation

If data came from a prompt, a local file, a script, a config, or a task — it does NOT go into documentation.
Documentation examples use only well-known, fictional, or obviously placeholder data.

### Approved Placeholders

| Category      | Examples                                    |
|---------------|---------------------------------------------|
| People        | Jane Doe, John Smith, Alice, Bob            |
| Email         | `jane.doe@example.com`, `admin@example.org` |
| Organizations | Acme Corp, Contoso, Northwind Traders       |
| Domains       | `example.com`, `example.org`, `example.net` |
| Addresses     | 123 Main Street, Suite 100, Springfield     |
| Phone         | `(555) 123-4567`                            |
| Accounts      | `demo-user`, `test-account`                 |
| Projects      | My Project, Sample App, Demo Tool           |

### Boundary

| Context                                             | Real Data Allowed?                    |
|-----------------------------------------------------|---------------------------------------|
| Local scripts and config files used at runtime      | Yes                                   |
| Git-ignored files with environment-specific values  | Yes                                   |
| Prompt data provided to build or configure the tool | Yes (in code only)                    |
| README.md, docs/, example templates                 | **No — placeholders only**            |
| CHANGELOG.md entries                                | **No — describe changes generically** |
| Code comments in committed source files             | **No — keep generic**                 |
