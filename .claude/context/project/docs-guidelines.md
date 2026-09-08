# Documentation Guidelines

> Where documentation lives, when to update it, and what must never appear in it.

## Two docs trees, one is live

| Tree | What it is |
|---|---|
| `website/` | The live documentation site. Astro and Starlight, published to GitHub Pages by `.github/workflows/docs.yml`. **This is the SDK's documentation.** |
| `docs/` | The legacy VuePress KurrentDB product site. Not the SDK's, and not maintained here. |

Write to `website/`. The workflow fires on any push that touches `website/**`.

Internal development docs go in neither tree. Designs, research, reports, and postmortems live under
`.claude/context/`.

## Keep docs in sync with code

Check whether the docs need an update when you:

- Add a feature or a public API
- Change an API method, an interface, or a signature
- Change a configuration option or an environment variable
- Change an installation or setup procedure
- Change a CLI command or a script
- Notice that a code example no longer matches the API

### Rules

- Update the docs in the same commit as the code change where you can.
- When the public API surface changes, check whether a page in `website/` needs it.
- Include a before-and-after example for a breaking change.
- **Run every code example before you commit it.** An example that no longer compiles is worse than no
  example. This is how docs rot, and it is the most common defect an audit finds.
- Link instead of duplicating. When one fact lives in two places, one copy goes stale.
- Do not document what does not exist yet. If a knob is `internal`, it is not a documented knob.

## Never leak real data

If data came from a prompt, a local file, a script, a config, or a task, it does **not** go into
documentation. Examples use values that are well known, fictional, or clearly marked as placeholders.

### Approved placeholders

| Category | Examples |
|---|---|
| People | Jane Doe, John Smith, Alice, Bob |
| Email | `jane.doe@example.com`, `admin@example.org` |
| Organizations | Acme Corp, Contoso, Northwind Traders |
| Domains | `example.com`, `example.org`, `example.net` |
| Addresses | 123 Main Street, Suite 100, Springfield |
| Phone | `(555) 123-4567` |
| Accounts | `demo-user`, `test-account` |
| Projects | My Project, Sample App, Demo Tool |

### Boundary

| Context | Real data allowed |
|---|---|
| Local scripts and config used at runtime | Yes |
| Git-ignored files holding environment-specific values | Yes |
| Prompt data used to build or configure the tool | Yes, in code only |
| `README.md`, `website/`, example templates | **No. Placeholders only.** |
| Release notes and changelog entries | **No. Describe the change in general terms.** |
| Code comments in committed source | **No. Keep them generic.** |
