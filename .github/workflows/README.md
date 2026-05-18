# CI/CD Workflows

This directory contains the GitHub Actions workflows that automate the build, test, and release
pipeline for the Kurrent .NET SDK.

## Workflow Overview

```
  PR opened ──► CI + Validate ──► merged to main ──► Main ──► tag v* ──► Release
                    │                                  │                    │
                    │                                  │                    │
               unit tests                        build + version       stable version
             + integration                      preview packages       NuGet + GitHub
               PR packages                       GitHub Packages
               validation
               labeling
```

## Workflows

### [`pull-request.yml`](pull-request.yml) -- Pull Request Pipeline

Triggers on pull requests and manual dispatch. Handles the full PR lifecycle: build, test, and
package. Source change detection skips the pipeline for docs-only changes.

**Event matrix:**

| Event                                              | What runs                              |
|----------------------------------------------------|----------------------------------------|
| PR opened/synchronize/ready_for_review (non-draft) | unit + integration tests, PR packages  |
| PR opened/synchronize (draft)                      | unit tests, PR packages                |
| Manual dispatch                                    | unit + integration tests               |

PR packages use the version format `pr.{number}.{run}.{sha}` and are published to GitHub Packages.

### [`validate-pr.yml`](validate-pr.yml) -- PR Validation

Triggers on pull requests (opened, edited, synchronize, ready_for_review). Validates the PR title
follows [conventional commits](https://www.conventionalcommits.org/) and applies type-based labels.

### [`main.yml`](main.yml) -- Main Branch Pipeline

Triggers on every push to `main`. Builds, versions, and publishes preview packages to GitHub
Packages.

The workflow detects whether the push came from a merged PR or a direct push:

| Source                            | Tests                                  | Publish                                  |
|-----------------------------------|----------------------------------------|------------------------------------------|
| Merged PR (committer: `web-flow`) | Skipped -- PR pipeline already validated | `preview.{run}` to GitHub Packages |
| Direct push (developer)           | Unit + integration tests               | `preview.{run}` to GitHub Packages       |

Both paths always publish preview packages, ensuring every commit on `main` has a corresponding
preview package available.

### [`release.yml`](release.yml) -- Stable Release Pipeline

Triggers on pushing a semver tag (`v*`) or manual dispatch. Uses the
[`bumpcraft`](../actions/bumpcraft/) action in `release` mode to compute a stable version, then
builds, publishes to both GitHub Packages and NuGet.org, and creates a GitHub Release.

| Trigger               | Version source       | What happens                                        |
|-----------------------|----------------------|-----------------------------------------------------|
| Tag push (`v1.2.3`)   | Tag name             | Build, publish, create GitHub Release               |
| Manual (with version) | Input parameter      | Tag, build, publish, create GitHub Release          |
| Manual (no version)   | Conventional commits | Compute next version, tag, build, publish           |
| Manual (dry run)      | Conventional commits | Compute version and build only -- nothing published |

### [`benchmark.yml`](benchmark.yml) -- Performance Benchmarks

Triggers on manual dispatch. Runs BenchmarkDotNet benchmarks, generates an HTML report, and uploads
results as artifacts.

## Package Version Scheme

| Pipeline | Version format                   | Example                 | Destination                 |
|----------|----------------------------------|-------------------------|-----------------------------|
| PR       | `{base}-pr.{number}.{run}.{sha}` | `1.0.0-pr.7.42.abc1234` | GitHub Packages             |
| Main     | `{base}-preview.{run}`           | `1.0.0-preview.42`      | GitHub Packages             |
| Release  | `{major}.{minor}.{patch}`        | `1.0.0`                 | GitHub Packages + NuGet.org |

All pipelines use the [`bumpcraft`](../actions/bumpcraft/) composite action. Pre-release builds
use MinVer with configurable templates. Release builds use `semver-bump` for conventional
commit analysis or accept an explicit version. See the
[action README](../actions/bumpcraft/README.md) for full documentation.

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

See [`actions/bumpcraft/version.yml`](../actions/bumpcraft/version.yml) for the full reference with
all options documented.
