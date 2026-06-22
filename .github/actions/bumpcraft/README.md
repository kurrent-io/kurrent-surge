# Bumpcraft

Computes a semver package version from git tags and a configuration file. Supports two modes:
**pre-release** for CI builds and **release** for stable publishing. Sets the version as step
outputs and writes a summary to the job log.

Delegates to [MinVer](https://github.com/adamralph/minver) for pre-release versioning and
[semver-bump](../../tools/semver-bump.cs) for release versioning — the configuration layer
abstracts the tools so they can be swapped without changing workflow files.

## Usage

```yaml
# Pre-release (default) — CI builds
- uses: ./.github/actions/bumpcraft
  id: version

# Pre-release — inline config (no version.yml needed)
- uses: ./.github/actions/bumpcraft
  id: version
  with:
    pre-release: '@dated'
    pull-request: '@detailed'

# Release — bump a component off the last tag (npm-style)
- uses: ./.github/actions/bumpcraft
  id: version
  with:
    mode: release
    bump: minor            # major | minor | patch | premajor | preminor | prepatch | prerelease
  env:
    GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}

# Release — explicit version (bump: custom)
- uses: ./.github/actions/bumpcraft
  id: version
  with:
    mode: release
    bump: custom
    version: ${{ inputs.version }}
```

## Inputs

| Input                 | Required | Default               | Description                                                                                                        |
|-----------------------|----------|-----------------------|--------------------------------------------------------------------------------------------------------------------|
| `mode`                | no       | `pre-release`         | `pre-release` for CI builds, `release` for stable publishing                                                       |
| `bump`                | no       | _(empty)_             | Release-mode component to bump off the last tag: `major`/`minor`/`patch`/`premajor`/`preminor`/`prepatch`/`prerelease`, or `custom` to use `version`. Ignored on tag-push releases. |
| `version`             | no       | _(empty)_             | Exact version for release mode when `bump` is `custom` (e.g., `1.2.3`)                                             |
| `config`              | no       | `.github/version.yml` | Path to the version configuration file. Falls back to the action's built-in defaults when the file does not exist. |
| `auto-increment`      | no       | _(from config)_       | Which semver component to bump: `major`, `minor`, or `patch`. Overrides config file.                               |
| `minimum-major-minor` | no       | _(from config)_       | Version floor as `major.minor` (e.g., `1.0`). Overrides config file.                                               |
| `pre-release`         | no       | _(from config)_       | Pre-release format preset or template for push events. Overrides config file.                                      |
| `pull-request`        | no       | _(from config)_       | Pre-release format preset or template for PR events. Overrides config file.                                        |

**Priority:** Action inputs > config file > built-in defaults.

## Outputs

| Output           | Example            | Description                                                  |
|------------------|--------------------|--------------------------------------------------------------|
| `version`        | `1.2.3-preview.42` | Full semver version string                                   |
| `is-pre-release` | `true`             | Whether this is a pre-release version                        |
| `tag`            | `v1.2.3`           | Git tag for this version                                     |
| `bump`           | `Minor`            | What drove the bump: Major, Minor, Patch (release mode only) |

## Release Mode

How the version is determined in release mode:

| Condition                    | Source                          | Requires token |
|------------------------------|---------------------------------|----------------|
| Tag push (`ref_type == tag`) | Git tag name                    | No             |
| `bump: custom`               | `version` input                 | No             |
| `bump: <component>`          | `semver-bump --bump` off last tag | Yes          |

`GITHUB_TOKEN` is only needed for the `bump: <component>` path, where `semver-bump` reads the
repository's tags through the GitHub API.

`semver-bump --bump` computes the next version from the last **stable** tag (npm `npm version`
semantics): `major`/`minor`/`patch` produce a stable bump, while the `pre*` variants and
`prerelease` append an incrementing `preview.N` counter. `prerelease` continues the highest
in-flight pre-release line above the last stable tag, opening a new one off the next patch when
none is in flight.

## Pre-release Mode

### Configuration

Override defaults using action inputs or a config file. All keys are optional.

**Option A — action inputs (no file needed):**

```yaml
- uses: ./.github/actions/bumpcraft
  with:
    auto-increment: patch
    minimum-major-minor: '1.0'
    pre-release: '@simple'
    pull-request: '@traceable'
```

**Option B — config file (`.github/version.yml`):**

```yaml
auto-increment: patch
minimum-major-minor: 1.0
pre-release: @simple
pull-request: @traceable
```

When both are provided, action inputs take priority over the config file per-key.

| Key                   | Values                    | Default      | Description                                     |
|-----------------------|---------------------------|--------------|-------------------------------------------------|
| `auto-increment`      | `major`, `minor`, `patch` | `patch`      | Which semver component to bump beyond a tag     |
| `minimum-major-minor` | Any `major.minor` string  | `1.0`        | Version floor — computed version is never lower |
| `pre-release`         | Preset or template        | `@simple`    | Pre-release format for push events (e.g., main) |
| `pull-request`        | Preset or template        | `@traceable` | Pre-release format for pull request events      |

### Presets

Use a preset prefixed with `@` instead of writing a raw template. Presets expand based on
which key they are assigned to:

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

### Template Variables

Build custom formats by combining variables:

| Variable    | Description                                    | Example   |
|-------------|------------------------------------------------|-----------|
| `{run}`     | Workflow run number (monotonically increasing) | `42`      |
| `{date}`    | UTC date as YYddd (2-digit year + day-of-year) | `26075`   |
| `{sha}`     | Short commit SHA (7 characters)                | `abc1234` |
| `{pr}`      | Pull request number                            | `7`       |
| `{commits}` | Number of commits in the pull request          | `15`      |
| `{attempt}` | Workflow run attempt (increments on re-runs)   | `1`       |

### Custom template example

```yaml
pre-release: preview.{date}.{run}
pull-request: pr.{pr}.{date}.{run}.{sha}
```

Produces:
- Push to main, run 42, March 16 2026: `1.0.0-preview.26075.42`
- PR #7, run 42, SHA abc1234: `1.0.0-pr.7.26075.42.abc1234`

## Defaults

When no `.github/version.yml` exists, the action uses its built-in
[`version.yml`](version.yml) which ships with:

```yaml
auto-increment: patch
minimum-major-minor: 1.0
pre-release: @simple
pull-request: @traceable
```
