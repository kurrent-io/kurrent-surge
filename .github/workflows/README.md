# CI/CD Workflows

This directory contains the GitHub Actions workflows that automate the build, test, and release
pipeline for the Kurrent .NET SDK.

## Workflow Overview

```
  PR opened ──► CI + PR Triage ──► merged to main ──► CI (push) ──► tag v* ──► Release
                    │                                   │                         │
                    │                                   │                         │
               unit tests                          build + version          stable version
             + integration                        preview packages          NuGet + GitHub
               PR packages                         GitHub Packages
               title + lint
```

`ci.yml` is the single build/test/package pipeline — it runs on both pull requests and pushes to
`main`, computing the right version for each event. The build job is capped at `timeout-minutes: 15`, and
`concurrency` with `cancel-in-progress` makes a new push supersede an in-flight run.

## Workflows

### [`ci.yml`](ci.yml) — Build, Test & Package

Triggers on pushes to `main`/`release/*`, pull requests to those branches, and manual dispatch. Two jobs
run. `markdown` lints every markdown file with
[markdownlint-cli2](https://github.com/DavidAnson/markdownlint-cli2), configured by
[`.markdownlint-cli2.jsonc`](../../.markdownlint-cli2.jsonc). `ci` builds, versions (via
[`bumpcraft`](../actions/bumpcraft/) in pre-release mode), packs and validates packages, runs tests, and
publishes preview/PR packages to GitHub Packages; path filters in
[`ci-path-filters.yml`](../ci-path-filters.yml) skip its work for changes outside the build surface.

**What runs:**

| Event                                   | Tests                      | Packages published          |
|-----------------------------------------|----------------------------|-----------------------------|
| PR — non-draft                          | unit + integration         | `pr.{number}.{run}.{sha}`   |
| PR — draft                              | unit                       | `pr.{number}.{run}.{sha}`   |
| Push to `main` / `release/*`            | unit + integration         | `preview.{run}`             |
| Manual dispatch                         | unit (+ integration if not draft) | per event               |

Integration tests and container setup are skipped on draft PRs. Packages go to GitHub Packages; the
version is chosen by `bumpcraft` from the triggering event (PR → `pr.*` template, push → `preview.*`).

### [`pr-triage.yml`](pr-triage.yml) — PR Triage

Triggers on `pull_request_target` (opened, edited, synchronize, ready_for_review). Metadata only — no job
in this workflow checks out or executes PR code, which is what makes the write token safe on forks.

| Job     | What it does                                                                                      |
|---------|---------------------------------------------------------------------------------------------------|
| `title` | Required gate. Validates the PR title as a [conventional commit](https://www.conventionalcommits.org/) via `amannn/action-semantic-pull-request`, since the title becomes the squash commit `semver-bump` reads. |
| `label` | Best-effort, never blocking. Labels by changed path via `actions/labeler` reading [`labeler.yml`](../labeler.yml), and by conventional-commit type via `actions/github-script`. |

### [`release.yml`](release.yml) — Stable Release Pipeline

Triggers on pushing a semver tag (`v*`) or manual dispatch. Uses the
[`bumpcraft`](../actions/bumpcraft/) action in `release` mode to compute a stable version, then builds,
publishes to both GitHub Packages and NuGet.org, and creates a GitHub Release. Capped at
`timeout-minutes: 10`.

| Trigger                      | Version source                    | What happens                                       |
|------------------------------|-----------------------------------|----------------------------------------------------|
| Tag push (`v1.2.3`)          | Tag name                          | Build, publish, create GitHub Release              |
| Manual (`bump: <component>`) | `semver-bump --bump` off last tag | Tag, build, publish, create GitHub Release         |
| Manual (`bump: custom`)      | `version` input                   | Tag, build, publish, create GitHub Release         |
| Manual (dry run)             | As selected above                 | Compute version and build only — nothing published |

### [`benchmark.yml`](benchmark.yml) — Performance Benchmarks

Triggers on manual dispatch, merge groups, pushes touching `perf/**`, and pull requests labeled
`run-benchmarks`. Runs BenchmarkDotNet on the self-hosted runners across a category matrix, generates an
HTML report, and uploads results as artifacts.

### Auxiliary workflows

- [`docs.yml`](docs.yml) — **Docs.** Builds the Starlight site in `website/` and publishes it to GitHub
  Pages, on pushes touching `website/**` + manual dispatch.
- [`devcontainer.yml`](devcontainer.yml) — **Build Devcontainer.** Builds and pushes the devcontainer
  image on pushes touching `.devcontainer/**` + manual dispatch.

## Package Version Scheme

| Pipeline           | Version format                   | Example                 | Destination                 |
|--------------------|----------------------------------|-------------------------|-----------------------------|
| CI — PR            | `{base}-pr.{number}.{run}.{sha}` | `1.0.0-pr.7.42.abc1234` | GitHub Packages             |
| CI — push to main  | `{base}-preview.{run}`           | `1.0.0-preview.42`      | GitHub Packages             |
| Release            | `{major}.{minor}.{patch}[-preview.N]` | `1.0.0` / `1.1.0-preview.1` | GitHub Packages + NuGet.org |

All pipelines use the [`bumpcraft`](../actions/bumpcraft/) composite action. Pre-release builds use MinVer
with configurable templates. Release builds bump a chosen component (npm-style) off the last tag via
`semver-bump`, or accept an explicit version. See the [action README](../actions/bumpcraft/README.md) for
full documentation.

## Version Configuration

Bumpcraft supports both **presets** and **custom templates** for pre-release identifiers.

### Presets

Use a preset name prefixed with `@` for common conventions:

| Preset       | `pre-release:`                     | `pull-request:`                 |
|--------------|------------------------------------|---------------------------------|
| `@simple`    | `preview.{run}`                    | `pr.{pr}.{run}`                 |
|              | → `1.0.0-preview.42`               | → `1.0.0-pr.7.42`               |
| `@traceable` | `preview.{run}.{sha}`              | `pr.{pr}.{run}.{sha}`           |
|              | → `1.0.0-preview.42.abc1234`       | → `1.0.0-pr.7.42.abc1234`       |
| `@dated`     | `preview.{date}.{run}`             | `pr.{pr}.{date}.{run}`          |
|              | → `1.0.0-preview.26075.42`         | → `1.0.0-pr.7.26075.42`         |
| `@detailed`  | `preview.{date}.{run}.{sha}`       | `pr.{pr}.{date}.{run}.{sha}`    |
|              | → `1.0.0-preview.26075.42.abc1234` | → `1.0.0-pr.7.26075.42.abc1234` |

Default configuration (no `.github/version.yml` needed):

```yaml
pre-release: @simple
pull-request: @traceable
```

### Custom Templates

Build your own format using template variables:

| Variable    | Description                                                 |
|-------------|-------------------------------------------------------------|
| `{run}`     | Workflow run number (monotonically increasing)              |
| `{date}`    | UTC date as YYddd (2-digit year + day-of-year, e.g., 26075) |
| `{sha}`     | Short commit SHA (7 characters)                             |
| `{pr}`      | Pull request number                                         |
| `{commits}` | Number of commits in the pull request                       |
| `{attempt}` | Workflow run attempt (increments on re-runs)                |

To override defaults, create `.github/version.yml`:

```yaml
auto-increment: patch
minimum-major-minor: 1.0
pre-release: preview.{date}.{run}
pull-request: pr.{pr}.{date}.{run}.{sha}
```

See [`actions/bumpcraft/version.yml`](../actions/bumpcraft/version.yml) for the full reference with all
options documented.
