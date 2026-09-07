# Contributing

Thanks for contributing to the Kurrent .NET SDK. This guide covers setup, the test runner, and the
commit and pull-request conventions. Engineering conventions (style, async rules, AOT constraints, module
layout) live in [`AGENTS.md`](AGENTS.md) — read that before writing code.

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
dotnet tool restore   # restore local tools used by tools/ and CI
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

Unit and integration tests live in the same project per package, split into `Unit/` and `Integration/`
folders; integration tests carry `[Category("Integration")]`. There are no separate `*.Integration.Tests`
projects.

## Commit messages

Commits follow [Conventional Commits v1.0.0](https://www.conventionalcommits.org/). The full type list, scope
tables, and examples are in [`.github/git-commit-instructions.md`](.github/git-commit-instructions.md).

```
<type>[optional scope]: <description>
```

- Past tense, lowercase, no trailing period — `feat(client.streams): added bidirectional read support`.
- Scopes are lowercase; use `package.module` for module-level precision (e.g. `client.registry`).
- Never put issue IDs (`DEV-XXXX`, `[DEV-1046]`) in the subject line — it breaks `semver-bump` parsing. Reference
  them in the body instead.

## Pull requests

1. Branch from `main`.
2. Implement the change and cover new or modified behavior with tests.
3. Run the affected suites green before pushing.
4. Open a PR. The [PR template](.github/PULL_REQUEST_TEMPLATE.md) carries the Definition of Done checklist —
   every new or modified `public` member needs XML documentation, and the full suite must pass.

The PR title becomes the squash commit on `main`, so it must itself be a valid conventional commit with no
issue IDs. Linear links PRs to issues via the branch name.

## Packaging

Validate package contents before proposing packaging changes with the verification gate:

```bash
python3 scripts/verify-package.py --csproj src/Kurrent.Surge/Kurrent.Surge.csproj
python3 scripts/verify-package.py --csproj src/Kurrent.Surge.Core/Kurrent.Surge.Core.csproj --list
```

Pass an existing `.nupkg` as a positional argument to verify a pre-built package instead of packing.

## Project layout

Publishable packages (no meta-package — consumers install only what they need):

| Package                                                             | What it does                                                       |
|---------------------------------------------------------------------|--------------------------------------------------------------------|
| [Kurrent.Surge](src/Kurrent.Surge/)                                 | gRPC client and stream processing — streams, schema registry, indexes, connectors, admin |
| [Kurrent.Surge.Core](src/Kurrent.Surge.Core/)                       | The contract layer, message declaration and the catalog generator, and the JSON, Protobuf, and Avro codecs, with no transport dependencies |

Everything else under `src/` (source generators, `Variant`, `Kurrent.CodeAnalysis`) is shared
solution infrastructure, not shipped as standalone packages. Proto definitions live in
`proto/kurrentdb/protocol/{v0,v1,v2}/`.

## Reporting issues and security

- Bugs and features: use the [issue templates](.github/ISSUE_TEMPLATE/) on GitHub.
- Vulnerabilities: follow [`SECURITY.md`](SECURITY.md) — do not open a public issue.
- Questions and help: see [`SUPPORT.md`](SUPPORT.md).

By contributing, you agree your contributions are licensed under the [Apache 2.0 License](LICENSE.md).
