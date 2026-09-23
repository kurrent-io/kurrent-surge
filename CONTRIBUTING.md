# Contributing

Thanks for contributing to Kurrent Surge. This guide covers setup, the test runner, and the
commit and pull-request conventions. [`.editorconfig`](.editorconfig) defines the code style.
[Module organization](.claude/context/project/module-organization.md) describes where files go in a module.

## Prerequisites

| Requirement | Notes                                                                                  |
|-------------|----------------------------------------------------------------------------------------|
| .NET SDK    | Pinned by [`global.json`](global.json) (`10.0.300`, `rollForward: latestMinor`). The SDK targets `net10.0` only. |
| Docker      | Required for integration tests. Containers and TLS certificates are provisioned automatically by the Aspire test harness — no manual cert setup. |

## Getting started

```bash
git clone https://github.com/kurrent-io/kurrent-surge.git
cd kurrent-surge

dotnet restore        # restore package dependencies
dotnet build -c Release
```

> Never pass `--framework` to a solution-wide `dotnet build`/`dotnet test`. It forces one TFM onto the whole
> solution and breaks the `netstandard2.0` source-generator projects with `NETSDK1005`.

## Running tests

Tests run on [TUnit](https://tunit.dev/) via the Microsoft Testing Platform — not VSTest. Use the test
runner, not `dotnet test`. The runner builds once (`-c Release`) then runs with `--no-build`.

```bash
dotnet scripts/testing/test-runner.cs -- run unit          # unit tests
dotnet scripts/testing/test-runner.cs -- run integration   # integration tests (needs Docker)
dotnet scripts/testing/test-runner.cs -- run all           # both
dotnet scripts/testing/test-runner.cs -- list unit         # list without running
```

Scope a run with a tree-node filter (`/Assembly/Namespace/Class/Method`, `*` wildcard):

```bash
dotnet scripts/testing/test-runner.cs -- run unit --treenode-filter "/*/*/ReadTests/*"
dotnet scripts/testing/test-runner.cs -- run unit --treenode-filter "/*/*/*/*[Category=Streams]"
```

Results are written to `.artifacts/test-results/<run-id>/` as `.trx` and markdown reports.

Unit and integration tests live in the same test project, split into `Unit/` and `Integration/`
folders; integration tests carry `[Category("Integration")]`. There are no separate `*.Integration.Tests`
projects.

## Commit messages

Commits follow [Conventional Commits v1.0.0](https://www.conventionalcommits.org/). The full type list, scope
tables, and examples are in [`.github/git-commit-instructions.md`](.github/git-commit-instructions.md).

```
<type>[optional scope]: <description>
```

- Past tense, lowercase, no trailing period — `feat(client.streams): added bidirectional read support`.
- Scopes are lowercase and name an area of the SDK. Use `area.module` for module-level precision, for
  example `client.registry`.
- Never put issue references (`#123`, `DEV-1046`) in the subject line. `semver-bump` does not recognize the
  subject, so a `feat` or a breaking change bumps only the patch version. Reference issues in the body instead.

## Pull requests

1. Branch from `main`.
2. Implement the change and cover new or modified behavior with tests.
3. Run the affected suites green before pushing.
4. Open a PR. The [PR template](.github/PULL_REQUEST_TEMPLATE.md) carries the Definition of Done checklist —
   every new or modified `public` member needs XML documentation, and the full suite must pass.

The PR title becomes the squash commit on `main`, so it must itself be a valid conventional commit with no
issue references.

## Packaging

Validate package contents before proposing packaging changes with the verification gate:

```bash
python3 scripts/verify-package.py --csproj src/Kurrent.Surge/Kurrent.Surge.csproj --list
```

Pass an existing `.nupkg` as a positional argument to verify a pre-built package instead of packing.

## Work on the docs site

The documentation site is in `docs/`. It uses Astro, Starlight, and Starwind UI. Install
[Bun](https://bun.sh), then start the dev server:

```bash
bun install --cwd docs
bun run --cwd docs dev
```

The dev server runs at `http://localhost:4400/kurrent-surge/` and reloads when you save a file.

### Optional agent tooling

If you use an AI coding agent on the site, these tools give it current Astro, Starwind, and
Tailwind documentation. None of them is required. Install the ones you want.

| Tool                   | What it gives the agent                  | Install (Claude Code)                                                                      |
|------------------------|------------------------------------------|--------------------------------------------------------------------------------------------|
| Astro docs MCP server  | Search over the Astro documentation      | `claude mcp add --transport http astro-docs https://mcp.docs.astro.build/mcp`              |
| Starwind UI MCP server | Starwind component docs and CLI commands | `claude mcp add starwind-ui -- npx -y @starwind-ui/mcp@0.5.1`                              |
| Impeccable             | UI design and review skills              | `/plugin marketplace add pbakaus/impeccable`, then `/plugin install impeccable@impeccable` |
| `tailwind-4-docs`      | Tailwind CSS v4 documentation            | `npx skills add Lombiq/Tailwind-Agent-Skills`                                              |

## Project layout

`Kurrent.Surge` is the only published package. The other projects in `src/` are not published. To list
the packable projects:

```bash
grep -l "IsPackable>true" src/*/*.csproj
```

Proto definitions live in `proto/kurrentdb/protocol/`.

## Reporting issues and security

- Bugs and features: use the [issue templates](.github/ISSUE_TEMPLATE/) on GitHub.
- Vulnerabilities: follow [`SECURITY.md`](SECURITY.md) — do not open a public issue.
- Questions and help: see [`SUPPORT.md`](SUPPORT.md).

By contributing, you agree your contributions are licensed under the [Apache 2.0 License](LICENSE.md).
