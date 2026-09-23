# Bumpcraft

Computes a semver package version with [semver-bump](../../../tools/semver-bump.cs) and a
configuration file. Two modes: **pre-release** for CI builds and **release** for publishing. The
action sets the version as step outputs and writes a summary to the job log.

Both modes start from the same rule. `semver-bump` reads the last stable tag and the conventional
commits since it. A `!` marker bumps the major, a `feat` bumps the minor, anything else bumps the
patch. The highest bump wins. The floor in `minimum-major-minor` raises a computed version that sits
below it. So the file holds the ground the numbering starts from, and the commits move it from there.

## Usage

```yaml
# Pre-release (default) — CI builds
- uses: ./.github/actions/bumpcraft
  id: version
  env:
    GITHUB_TOKEN: ${{ github.token }}

# Pre-release — inline config (no version.yml needed)
- uses: ./.github/actions/bumpcraft
  id: version
  with:
    pre-release: '@dated'
    pull-request: '@detailed'
  env:
    GITHUB_TOKEN: ${{ github.token }}

# Release — the commits decide
- uses: ./.github/actions/bumpcraft
  id: version
  with:
    mode: release
    bump: auto
  env:
    GITHUB_TOKEN: ${{ github.token }}

# Release — bump one component off the last stable tag
- uses: ./.github/actions/bumpcraft
  id: version
  with:
    mode: release
    bump: minor            # major | minor | patch | premajor | preminor | prepatch | prerelease
  env:
    GITHUB_TOKEN: ${{ github.token }}

# Release — explicit version (bump: custom)
- uses: ./.github/actions/bumpcraft
  id: version
  with:
    mode: release
    bump: custom
    version: ${{ inputs.version }}
```

`GITHUB_TOKEN` is needed in every mode except `bump: custom` and a tag push, because `semver-bump`
reads the repository's tags and commits through the GitHub API. The job's default token with
`contents: read` is enough.

## Inputs

| Input                 | Required | Default               | Description                                                                                                        |
|-----------------------|----------|-----------------------|--------------------------------------------------------------------------------------------------------------------|
| `mode`                | no       | `pre-release`         | `pre-release` for CI builds, `release` for publishing                                                              |
| `bump`                | no       | _(empty)_             | Release mode only. `auto` or empty: the commits decide. A component: `major`, `minor`, `patch`, `premajor`, `preminor`, `prepatch`, `prerelease`. `custom`: use `version`. Ignored on tag pushes |
| `version`             | no       | _(empty)_             | Exact version for release mode when `bump` is `custom` (e.g., `1.2.3`)                                             |
| `config`              | no       | `.github/version.yml` | Path to the version configuration file. Falls back to the action's built-in defaults when the file does not exist. |
| `minimum-major-minor` | no       | _(from config)_       | Version floor as `major.minor` (e.g., `2.0`). Overrides config file.                                               |
| `pre-release`         | no       | _(from config)_       | Pre-release format preset or template for push events. Overrides config file.                                      |
| `pull-request`        | no       | _(from config)_       | Pre-release format preset or template for PR events. Overrides config file.                                        |

**Priority:** Action inputs > config file > built-in defaults.

## Outputs

| Output           | Example            | Description                                        |
|------------------|--------------------|----------------------------------------------------|
| `version`        | `2.0.0-preview.42` | Full semver version string                         |
| `is-pre-release` | `true`             | Whether this is a pre-release version              |
| `tag`            | `v2.0.0`           | Git tag for this version                           |
| `bump`           | `Minor`            | What drove the bump: Major, Minor, Patch           |

## Release mode

| Condition                    | Source                                                    | Requires token |
|------------------------------|-----------------------------------------------------------|----------------|
| Tag push (`ref_type == tag`) | Git tag name                                              | No             |
| `bump: custom`               | `version` input                                           | No             |
| `bump: auto`, or empty       | The conventional commits since the last stable tag         | Yes            |
| `bump: <component>`          | That component, bumped off the last stable tag             | Yes            |

`auto` is what a normal release uses. `main` publishes previews of the same version, so the release
ships the version the previews carried, without the pre-release identifiers.

A component bump follows `npm version` semantics: `major`, `minor`, `patch` produce a stable bump,
and the `pre*` variants append an incrementing `preview.N` counter taken from the existing tags.
`prerelease` continues the highest in-flight pre-release line above the last stable tag, and opens a
new one off the next patch when none is in flight.

The floor applies to every computed version. An explicit version is published as typed.

## Pre-release mode

The version is the same computed version, with the resolved template appended as the pre-release
identifiers. Nothing is added to the template: `preview.{run}` becomes exactly `preview.42`.

### Configuration

Override defaults using action inputs or a config file. All keys are optional.

**Option A — action inputs (no file needed):**

```yaml
- uses: ./.github/actions/bumpcraft
  with:
    minimum-major-minor: '2.0'
    pre-release: '@simple'
    pull-request: '@traceable'
```

**Option B — config file (`.github/version.yml`):**

```yaml
minimum-major-minor: 2.0
pre-release: @simple
pull-request: @traceable
```

When both are provided, action inputs take priority over the config file per-key.

| Key                   | Values                    | Default      | Description                                     |
|-----------------------|---------------------------|--------------|-------------------------------------------------|
| `minimum-major-minor` | Any `major.minor` string  | `1.0`        | Version floor — computed version is never lower |
| `pre-release`         | Preset or template        | `@simple`    | Pre-release format for push events (e.g., main) |
| `pull-request`        | Preset or template        | `@traceable` | Pre-release format for pull request events      |

### Presets

Use a preset prefixed with `@` instead of writing a raw template. Presets expand based on
which key they are assigned to:

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

### Template variables

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

- Push to main, run 42, March 16 2026: `2.0.0-preview.26075.42`
- PR #7, run 42, SHA abc1234: `2.0.0-pr.7.26075.42.abc1234`

## Defaults

When no `.github/version.yml` exists, the action uses its built-in
[`version.yml`](version.yml) which ships with:

```yaml
minimum-major-minor: 1.0
pre-release: @simple
pull-request: @traceable
```
