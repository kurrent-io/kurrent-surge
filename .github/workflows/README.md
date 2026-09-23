# CI/CD Workflows

This directory contains the GitHub Actions workflows that automate the build, test, and release
pipeline for Kurrent Surge.

## Workflow Overview

```
  PR opened ──► CI + PR Triage ──► merged to main ──► CI (push) ──────────► Release (dispatch or tag)
                    │                                   │                         │
                    │                                   │                         │
               unit tests                          build + test              stable version
             + integration                       preview published          published to
               PR packages                        to nuget.org                nuget.org
               packed only                                                  + GitHub Release
```

`ci.yml` is the single build/test/package pipeline. It runs on pull requests and on pushes to `main`,
and computes the version for each event. The build job is capped at `timeout-minutes: 15`. A new push
to a pull request supersedes its in-flight run. Pushes to `main` queue instead, because a run on `main`
ends in a publish.

## Workflows

### [`ci.yml`](ci.yml) — Build, Test & Publish Preview

Triggers on pushes to `main`/`release/*`, pull requests to those branches, and manual dispatch. Three
jobs run. `markdown` lints every markdown file with
[markdownlint-cli2](https://github.com/DavidAnson/markdownlint-cli2), configured by
[`.markdownlint-cli2.jsonc`](../../.markdownlint-cli2.jsonc). `ci` builds, versions (via
[`bumpcraft`](../actions/bumpcraft/) in pre-release mode), packs and validates the package, and runs
tests. `publish` runs after a green `ci` on a push to `main` and pushes that run's package to
nuget.org.

Path filters in [`ci-path-filters.yml`](../ci-path-filters.yml) skip the work for changes outside the
build surface.

**What runs:**

| Event                                   | Tests                             | Package                                   |
|-----------------------------------------|-----------------------------------|-------------------------------------------|
| PR — non-draft                          | unit + integration                | `pr.{number}.{run}.{sha}`, packed only     |
| PR — draft                              | unit                              | `pr.{number}.{run}.{sha}`, packed only     |
| Push to `main`                          | unit + integration                | `preview.{run}`, published to nuget.org    |
| Push to `release/*`                     | unit + integration                | `preview.{run}`, packed only               |
| Manual dispatch                         | unit (+ integration if not draft) | per event, packed only                     |

Integration tests and container setup are skipped on draft PRs. A pull request never publishes: the
`publish` job runs only for `push` events on `main`, inside the `preview` GitHub environment, which is
restricted to `main`. The nuget.org trusted publishing policy for `ci.yml` requires that environment.

### [`pr-triage.yml`](pr-triage.yml) — PR Triage

Triggers on `pull_request_target` (opened, edited, synchronize, ready_for_review). Metadata only — no job
in this workflow checks out or executes PR code, which is what makes the write token safe on forks.

| Job     | What it does                                                                                      |
|---------|---------------------------------------------------------------------------------------------------|
| `title` | Required gate. Validates the PR title as a [conventional commit](https://www.conventionalcommits.org/) via `amannn/action-semantic-pull-request`, since the title becomes the squash commit `semver-bump` reads. |
| `label` | Best-effort, never blocking. Labels by changed path via `actions/labeler` reading [`labeler.yml`](../labeler.yml), and by conventional-commit type via `actions/github-script`. |

### [`release.yml`](release.yml) — Stable Release Pipeline

Triggers on pushing a semver tag (`v*`) or manual dispatch. Uses the
[`bumpcraft`](../actions/bumpcraft/) action in `release` mode to compute the version, then builds,
publishes to nuget.org, and creates a GitHub Release. Capped at `timeout-minutes: 10`.

| Trigger                      | Version source                                              | What happens                                       |
|------------------------------|-------------------------------------------------------------|----------------------------------------------------|
| Manual (`bump: auto`, default) | The conventional commits since the last stable tag        | Tag, build, publish, create GitHub Release         |
| Manual (`bump: <component>`) | That component, bumped off the last stable tag              | Tag, build, publish, create GitHub Release         |
| Manual (`bump: custom`)      | `version` input                                             | Tag, build, publish, create GitHub Release         |
| Tag push (`v1.2.3`)          | Tag name                                                    | Build, publish, create GitHub Release              |
| Manual (dry run)             | As selected above                                           | Compute version and build only — nothing published |

`auto` ships the version that `main` publishes as previews, without the pre-release identifiers.
`dry_run` is on by default.

### [`benchmark.yml`](benchmark.yml) — Performance Benchmarks

Triggers on manual dispatch only. Dispatch needs write access to the repository. Never runs on a push or
a pull request, so no fork code reaches the self-hosted runners. Runs BenchmarkDotNet on the self-hosted
runners across a category matrix, generates an HTML report, and uploads the results as artifacts.

### Auxiliary workflows

- [`docs.yml`](docs.yml) — **Docs.** Builds the Starlight site in `docs/` and publishes it to GitHub
  Pages, on pushes touching `docs/**` + manual dispatch.
- [`devcontainer.yml`](devcontainer.yml) — **Build Devcontainer.** Builds and pushes the devcontainer
  image on pushes touching `.devcontainer/**` + manual dispatch.

## Publishing Credentials

Neither workflow holds a nuget.org API key. Both use
[trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): the job presents
its GitHub OIDC token, nuget.org matches it against a policy, and returns a key that lives for one hour.
Three things must agree, or `NuGet/login` fails with a policy-not-found error.

1. **A nuget.org user** that is a member of the `kurrent` organization on nuget.org. Collaborator is
   enough.
2. **Two trusted publishing policies** in that user's account. nuget.org → account menu → Trusted
   Publishing → Create. The owner of a policy is the account that creates it, so the user creates them
   while signed in.

   | Field                      | Preview                                   | Release                                   |
   |----------------------------|-------------------------------------------|-------------------------------------------|
   | Policy Name                | `Kurrent Surge (Preview)`                 | `Kurrent Surge (Release)`                 |
   | Package Owner              | `kurrent`                                 | `kurrent`                                 |
   | CI/CD Provider             | GitHub Actions                            | GitHub Actions                            |
   | Repository Owner           | `kurrent-io`                              | `kurrent-io`                              |
   | Repository                 | `kurrent-surge`                           | `kurrent-surge`                           |
   | Workflow File              | `ci.yml`                                  | `release.yml`                             |
   | Environment                | `preview`                                 | `release`                                 |
   | Scopes                     | Push: new packages and package versions   | Push: new packages and package versions   |
   | Unlist or relist           | off                                       | off                                       |
   | Glob Patterns and Packages | `Kurrent.Surge*`                          | `Kurrent.Surge*`                          |

   The Environment field is what stops a pull request from publishing. GitHub lets only `main` into
   the `preview` environment, and nuget.org refuses a token without that environment claim.

3. **The repository variable `NUGET_USER`** set to that nuget.org username. Both workflows pass it to
   `NuGet/login`.

   ```bash
   gh variable set NUGET_USER --body "<nuget.org username>"
   ```

A new policy starts as **temporarily active for 7 days** while the repository is private. The first
publish through it makes it permanent. "Activate for 7 days" on the policy reopens the window.

**Recovery.** A policy is tied to the account that created it. If that person leaves the `kurrent`
organization, both policies go inactive and every publish fails at `NuGet/login`. Another member of
`kurrent` creates the same two policies in their own account, activates them, and the variable moves:
`gh variable set NUGET_USER --body "<their username>"`. Publishing resumes on the next run. A second
member can create the policies in advance as a standby. Their copies show inactive after 7 days unused.
One click on "Activate for 7 days" brings them back on the day of the switch.

## Package Version Scheme

| Pipeline           | Version format                        | Example                     | Destination |
|--------------------|---------------------------------------|-----------------------------|-------------|
| CI — PR            | `{next}-pr.{number}.{run}.{sha}`      | `2.0.0-pr.7.42.abc1234`     | Packed only |
| CI — push to main  | `{next}-preview.{run}`                | `2.0.0-preview.42`          | nuget.org   |
| Release            | `{next}`, or a chosen component, or a typed version | `2.0.0`, `2.1.0`  | nuget.org + GitHub Release |

`{next}` is one rule for every pipeline: the last stable tag, bumped by the conventional commits since
it. A `!` marker bumps the major, a `feat` bumps the minor, anything else the patch. The floor in
[`.github/version.yml`](../version.yml) raises a computed version that sits below it, so numbering starts
at `2.0.0` and never falls below it.

`main` publishes `{next}` as previews. A release publishes the same `{next}` without the pre-release
identifiers. All pipelines use the [`bumpcraft`](../actions/bumpcraft/) composite action, which calls
[`semver-bump`](../../tools/semver-bump.cs). See the [action README](../actions/bumpcraft/README.md).

## Version Configuration

`.github/version.yml` holds three keys. `minimum-major-minor` is the floor. `pre-release` is the
template for pushes. `pull-request` is the template for pull requests. A template is a **preset** or a
**custom template** for the pre-release identifiers.

### Presets

Use a preset name prefixed with `@` for common conventions:

| Preset       | `pre-release:`                     | `pull-request:`                 |
|--------------|------------------------------------|---------------------------------|
| `@simple`    | `preview.{run}`                    | `pr.{pr}.{run}`                 |
|              | → `2.0.0-preview.42`               | → `2.0.0-pr.7.42`               |
| `@traceable` | `preview.{run}.{sha}`              | `pr.{pr}.{run}.{sha}`           |
|              | → `2.0.0-preview.42.abc1234`       | → `2.0.0-pr.7.42.abc1234`       |
| `@dated`     | `preview.{date}.{run}`             | `pr.{pr}.{date}.{run}`          |
|              | → `2.0.0-preview.26075.42`         | → `2.0.0-pr.7.26075.42`         |
| `@detailed`  | `preview.{date}.{run}.{sha}`       | `pr.{pr}.{date}.{run}.{sha}`    |
|              | → `2.0.0-preview.26075.42.abc1234` | → `2.0.0-pr.7.26075.42.abc1234` |

Default configuration (no `.github/version.yml` needed):

```yaml
minimum-major-minor: 1.0
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
minimum-major-minor: 2.0
pre-release: preview.{date}.{run}
pull-request: pr.{pr}.{date}.{run}.{sha}
```

See [`actions/bumpcraft/version.yml`](../actions/bumpcraft/version.yml) for the full reference with all
options documented.
