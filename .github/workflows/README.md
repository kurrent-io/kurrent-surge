# CI/CD Workflows

This directory contains the GitHub Actions workflows that automate the build, test, and release
pipeline for the Kurrent .NET SDK.

## Workflow Overview

```
  PR opened ──► CI + CI-Checks ──► merged to main ──► CI (push) ──► tag v* ──► Release
                    │                                   │                         │
                    │                                   │                         │
               unit tests                          build + version          stable version
             + integration                        preview packages          NuGet + GitHub
               PR packages                         GitHub Packages
               title + lint
```

`ci.yml` is the single build/test/package pipeline — it runs on both pull requests and pushes to
`main`, computing the right version for each event. Jobs are capped at `timeout-minutes: 10` and use
`concurrency` with `cancel-in-progress` so a new push supersedes an in-flight run.

## Workflows

### [`ci.yml`](ci.yml) — Build, Test & Package

Triggers on pushes to `main`/`release/*`, pull requests to those branches, and manual dispatch. Source
change detection skips the pipeline for docs-only changes. One job builds, versions (via
[`bumpcraft`](../actions/bumpcraft/) in pre-release mode), packs and validates packages, runs tests, and
publishes preview/PR packages to GitHub Packages.

**What runs:**

| Event                                   | Tests                      | Packages published          |
|-----------------------------------------|----------------------------|-----------------------------|
| PR — non-draft                          | unit + integration         | `pr.{number}.{run}.{sha}`   |
| PR — draft                              | unit                       | `pr.{number}.{run}.{sha}`   |
| Push to `main` / `release/*`            | unit + integration         | `preview.{run}`             |
| Manual dispatch                         | unit (+ integration if not draft) | per event               |

Integration tests and container setup are skipped on draft PRs. Packages go to GitHub Packages; the
version is chosen by `bumpcraft` from the triggering event (PR → `pr.*` template, push → `preview.*`).

### [`ci-checks.yml`](ci-checks.yml) — PR Validation

Triggers on `pull_request_target` (opened, edited, synchronize, ready_for_review). Validates the PR title
follows [conventional commits](https://www.conventionalcommits.org/) (the
[`conventional-commits`](../actions/conventional-commits/) action) and runs markdownlint.

### [`release.yml`](release.yml) — Stable Release Pipeline

Triggers on pushing a semver tag (`v*`) or manual dispatch. Uses the
[`bumpcraft`](../actions/bumpcraft/) action in `release` mode to compute a stable version, then builds,
publishes to both GitHub Packages and NuGet.org, and creates a GitHub Release. Capped at
`timeout-minutes: 10`.

| Trigger               | Version source       | What happens                                        |
|-----------------------|----------------------|-----------------------------------------------------|
| Tag push (`v1.2.3`)   | Tag name             | Build, publish, create GitHub Release               |
| Manual (with version) | Input parameter      | Tag, build, publish, create GitHub Release          |
| Manual (no version)   | Conventional commits | Compute next version, tag, build, publish           |
| Manual (dry run)      | Conventional commits | Compute version and build only — nothing published  |

### [`benchmark.yml`](benchmark.yml) — Performance Benchmarks

Triggers on manual dispatch. Runs BenchmarkDotNet benchmarks, generates an HTML report, and uploads
results as artifacts.

### Auxiliary workflows

- [`labeler.yml`](labeler.yml) — **Pull Request Labeler.** Applies labels to PRs (`pull_request_target` +
  manual dispatch).
- [`prime-runners.yml`](prime-runners.yml) — **Prime Runners.** Daily cron (06:00 UTC) + manual dispatch;
  warms the self-hosted runners' tool and package caches so real pipeline runs start warm.
- [`devcontainer.yml`](devcontainer.yml) — **Build Devcontainer.** Builds and pushes the devcontainer
  image on relevant pushes + manual dispatch.

## Package Version Scheme

| Pipeline           | Version format                   | Example                 | Destination                 |
|--------------------|----------------------------------|-------------------------|-----------------------------|
| CI — PR            | `{base}-pr.{number}.{run}.{sha}` | `1.0.0-pr.7.42.abc1234` | GitHub Packages             |
| CI — push to main  | `{base}-preview.{run}`           | `1.0.0-preview.42`      | GitHub Packages             |
| Release            | `{major}.{minor}.{patch}`        | `1.0.0`                 | GitHub Packages + NuGet.org |

All pipelines use the [`bumpcraft`](../actions/bumpcraft/) composite action. Pre-release builds use MinVer
with configurable templates. Release builds use `semver-bump` for conventional commit analysis or accept an
explicit version. See the [action README](../actions/bumpcraft/README.md) for full documentation.

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
