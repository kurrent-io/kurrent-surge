# ghactions — GitHub Actions TUI Design

A cross-platform terminal UI app for macOS and Linux that replaces the GitHub Actions web interface.
Trigger workflows, monitor runs in real time, stream logs, and manage CI from the terminal.

## Contents

- [Purpose](#purpose)
- [Technology Stack](#technology-stack)
- [Visual Design](#visual-design)
- [Layout Architecture](#layout-architecture)
- [Runs Column](#runs-column)
- [Detail Panel](#detail-panel)
- [GitHub API Integration](#github-api-integration)
- [Authentication](#authentication)
- [Settings](#settings)
- [Keybindings](#keybindings)
- [Features — v1 Scope](#features--v1-scope)

## Purpose

The GitHub Actions web UI is slow to navigate, has no offline capability, and lacks the ergonomics
developers expect from a CI tool. `ghactions` is a keyboard-driven TUI that surfaces the information
you actually need: run status, step progress, logs, and trigger controls, all without leaving the
terminal.

## Technology Stack

| Concern        | Choice                   | Rationale                                              |
| -------------- | ------------------------ | ------------------------------------------------------ |
| TUI framework  | Terminal.Gui v2 (.NET 8) | Full widget system, mouse support, themeable, pure C#  |
| GitHub API     | Octokit.NET              | Official .NET SDK, typed, well-maintained              |
| Auth           | Multi-source (see below) | Zero friction for the common case                      |
| Target runtime | .NET 8 / 9 / 10          | Matches the existing Kurrent.Client toolchain          |
| Platforms      | macOS, Linux             | No Electron, no runtime overhead beyond .NET           |

## Visual Design

### Theme: Mission Control

Inspired by NASA mission control displays. Technical, high-contrast, unambiguous.

- **Background**: `#030508` (near-black, neutral)
- **Panel background**: `#070a14`
- **Borders**: `#1c2535`
- **Primary accent**: `#f5a623` amber. Used for active panel titles, app name, log section headers.
- **Running accent**: `#00aaff` electric blue. Used for in-progress states only.
- **Success**: `#00cc66` green
- **Failure**: `#ff4444` red
- **Branch names**: `#3a8a88` teal. Distinct hue, not a blue-grey variant.

### Typography

- **Body**: IBM Plex Mono — technical, readable, wide character support
- **App name / titles**: Orbitron — futuristic, high-legibility at small sizes

### Five-level grey hierarchy

Every element maps to exactly one level. No two adjacent elements share the same level.

| Level       | Hex       | Used for                                    |
| ----------- | --------- | ------------------------------------------- |
| text-ghost  | `#364050` | Panel sub-labels, pending steps             |
| text-dim    | `#5a6a7a` | Secondary meta (duration, actor, kbd hints) |
| text-body   | `#9aaabb` | Commit messages, body text, repo tag        |
| text-hi     | `#dce8f4` | Selected/active items, commit subject       |
| text-stamp  | `#2a3848` | Timestamps, faintest visible text           |

### Keyboard shortcut style

Shortcuts are rendered as `#9aaabb` (text-body) so they are clearly readable. The `kbd` key itself
uses amber `#f5a623` on `#0d1220` with a `#2a3848` border — high contrast, intentional.

### Additional themes (v1, selectable in Settings > Display)

- Mission Control (default)
- Phosphor (green phosphor CRT)
- Monochrome (neutral black/white)
- Alert Red (red accent)
- Paper (light background)
- Synthwave (purple accent)

## Layout Architecture

Three-column bento grid with a uniform 46px header row across all columns.

```
┌─────────────────────────────────────────────────────────────────┐
│  GH:ACTIONS   kurrent-io/KurrentDB  ● LIVE        D R X S Q   │
├──────────────┬──────────────────────┬──────────────────────────┤
│ WORKFLOWS  6 │ RUNS — CI  20,298    │ DETAIL  LOGS  JOBS  INFO │  ← 46px, uniform
├──────────────┼──────────────────────┼─────────────┬────────────┤
│              │  [event filter strip]│  Steps      │  Log       │
│  Workflow    │                      │  panel      │  output    │
│  list        │  [type col] Run list │  (collaps.) │  panel     │
│              │                      │             │            │
├──────────────┴──────────────────────┴─────────────┴────────────┤
│ STATUS  ● 1 running │ REPO kurrent-io/KurrentDB │ REFRESH 10s │
└─────────────────────────────────────────────────────────────────┘
```

### Column widths

Default widths; all splitters are user-resizable via `Alt+←→` or mouse drag. Saved to config.

| Column    | Default width |
| --------- | ------------- |
| Workflows | 220px         |
| Runs      | 400px         |
| Detail    | remaining     |

### Header row

All three column headers share exactly `height: 46px` with `overflow: hidden`. The active-tab
indicator in the Detail column uses `box-shadow: inset 0 -3px 0 0 #00aaff` so it has zero layout
impact and cannot cause height drift.

## Runs Column

### Event filter strip

A compact 32px strip between the header and the run list. Filters by GitHub trigger event.

| Pill        | Icon | Colour  | GitHub event        |
| ----------- | ---- | ------- | ------------------- |
| PR          | ⎇   | violet  | `pull_request`      |
| Push        | ↑    | amber   | `push`              |
| Manual      | ▶    | blue    | `workflow_dispatch` |
| Schedule    | ⏱   | teal    | `schedule`          |

Active pills glow with their type colour. Toggle with keyboard or mouse. Press `F` to focus the
filter strip; `Space` toggles the focused pill; `Esc` returns focus to the run list.

A `/` shortcut opens inline text search scoped to commit messages, branch names, and actors.

### Run row structure

Each run uses a two-column grid: a narrow **type column** on the left and the **run content** on
the right.

```
┌────────┬──────────────────────────────────────────────┐
│  ✦     │  ⟳ running   add retry on transient errors   │
│  FEAT  │  ⎇ PR #412   feat/retry-logic   1m 44s       │
│        │  [enhancement]  [needs review]               │
└────────┴──────────────────────────────────────────────┘
```

The type column is **collapsible**. Collapsed: shows icon only (24px wide). Expanded: shows icon
and uppercase label (72px wide). Toggle with `\`. Width preference is saved to config.

### Conventional commit type system

The app parses the conventional commit prefix from the run's head commit message. When a prefix is
recognised the type badge is shown; unrecognised commits show no badge.

| Type       | Icon | Colour    | Hex       |
| ---------- | ---- | --------- | --------- |
| `feat`     | ✦    | blue      | `#3b82f6` |
| `fix`      | ⚑    | red       | `#ef4444` |
| `chore`    | ⚙    | gray      | `#4b5563` |
| `docs`     | ≡    | violet    | `#8b5cf6` |
| `refactor` | △    | amber     | `#f59e0b` |
| `test`     | ◈    | emerald   | `#10b981` |
| `ci`       | ⚡   | cyan      | `#06b6d4` |
| `perf`     | ▲    | orange    | `#f97316` |
| `style`    | ◇    | pink      | `#ec4899` |
| `build`    | ⬡    | teal      | `#14b8a6` |
| `revert`   | ↩    | deep red  | `#dc2626` |

### Run row content

- **Row 1**: status chip + commit message (prefix stripped)
- **Row 2**: event badge + branch (teal) + duration + actor
- **Row 3** (optional): GitHub PR labels as rounded chips

## Detail Panel

Three tabs: `LOGS`, `JOBS`, `INFO`. Switch with `1`/`2`/`3` or `Tab`.

### LOGS tab

Inner split: steps panel (190px, collapsible) on the left and log output on the right.

Steps panel shows each step with status icon, name, and duration:
- Pending: `○` in `#364050`
- Running: `⟳` in `#00aaff` (animated spin)
- Done: `✓` in `#00cc66`
- Failed: `✗` in `#ff4444`

Log output shows timestamped lines. Timestamps in `#2a3848` (faintest). Commands in `#00aaff`.
Normal output in `#5a6a7a`. Highlighted lines (project names, key output) in `#9aaabb`. A blinking
block cursor marks the live position during active streaming.

### JOBS tab

Lists all jobs in the selected run with their status and duration. Selecting a job updates the
steps panel and log output to reflect that job.

### INFO tab

Full commit and run metadata.

- **Commit**: subject line (bright), body text (dim)
- **Run metadata**: SHA, trigger event, branch, author, start time, run ID
- **Labels**: GitHub PR labels rendered as rounded chips

## GitHub API Integration

GitHub's REST API does not provide WebSocket streaming for logs. Real-time behaviour is achieved
through polling.

| Concern             | Endpoint                                              | Interval  |
| ------------------- | ----------------------------------------------------- | --------- |
| Workflow list       | `GET /repos/{owner}/{repo}/actions/workflows`         | on open   |
| Run list            | `GET /repos/{owner}/{repo}/actions/runs`              | 10s       |
| In-progress run     | `GET /repos/{owner}/{repo}/actions/runs/{id}`         | 10s       |
| Job + step status   | `GET /repos/{owner}/{repo}/actions/runs/{id}/jobs`    | 5s        |
| Live job logs       | `GET /repos/{owner}/{repo}/actions/jobs/{id}/logs`    | 5s        |
| Completed job logs  | `GET /repos/{owner}/{repo}/actions/jobs/{id}/logs`    | once      |
| PR labels           | `GET /repos/{owner}/{repo}/pulls/{pr_number}`         | on select |

Polling is stopped when the app loses focus or the selected run reaches a terminal state.

## Authentication

Resolution order (first match wins):

1. `gh` CLI token — reads `~/.config/gh/hosts.yml`
2. `GITHUB_TOKEN` environment variable
3. PAT stored in system keychain (macOS Keychain / libsecret on Linux)
4. PAT in config file (`~/.config/ghactions/config.toml`)

Token scopes required: `repo`, `workflow`, `read:org`.

On startup, the resolved token is validated against the GitHub API. A startup error is shown if
validation fails, with instructions for each auth method.

## Settings

Accessed via `S` from anywhere in the app. Navigation uses arrow keys; `Esc` returns to the
dashboard.

| Section       | Key settings                                                           |
| ------------- | ---------------------------------------------------------------------- |
| Authentication| Token strategy, PAT entry, keychain storage, startup validation        |
| Repositories  | Default repo override, additional repos (future multi-repo support)    |
| Display       | Theme selector (6 themes), column default widths, type column mode     |
| Polling       | Run list interval, log interval, pause-on-blur toggle                  |
| Keybindings   | Rebind any action to a custom key                                       |
| About         | Version, GitHub link, update check                                     |

## Keybindings

### Global

| Key       | Action                        |
| --------- | ----------------------------- |
| `S`       | Open settings                 |
| `Q`       | Quit                          |
| `Tab`     | Cycle focus between panels    |
| `?`       | Show keybinding help overlay  |

### Runs panel

| Key       | Action                                     |
| --------- | ------------------------------------------ |
| `↑↓`      | Navigate runs                              |
| `Enter`   | Select run, focus detail panel             |
| `D`       | Trigger workflow dispatch dialog           |
| `R`       | Re-run selected run (or failed jobs only)  |
| `X`       | Cancel in-progress run                     |
| `F`       | Focus event filter strip                   |
| `/`       | Open inline search                         |
| `\`       | Toggle type column (icon-only / full)      |
| `1` `2` `3` | Switch detail tab (Logs / Jobs / Info)   |

### Detail panel

| Key    | Action                        |
| ------ | ----------------------------- |
| `↑↓`  | Scroll log output             |
| `G`    | Jump to bottom (follow mode)  |
| `Esc`  | Return focus to runs panel    |

### Panel resizing

| Key         | Action                          |
| ----------- | ------------------------------- |
| `Alt+←→`   | Resize focused splitter         |
| Mouse drag  | Drag splitter to resize         |

## Features — v1 Scope

In scope:

- Workflow list with last-run status indicator
- Run history per workflow (paginated, 20 per page)
- Event filter strip (PR / push / manual / schedule)
- Conventional commit type badges and collapsible type column
- GitHub PR labels on run rows
- Real-time log streaming via polling
- Step-by-step progress in the steps panel
- Workflow dispatch dialog with input fields
- Re-run failed jobs or entire run
- Cancel in-progress runs
- Commit and PR detail in the Info tab
- Six selectable themes
- User-resizable panels (width saved to config)
- Multi-source authentication
- Auto-detect repo from `git remote origin`

Out of scope for v1:

- Multi-repo / org-wide dashboard
- Notifications / system tray integration
- Artifact download
- Secret management
- Annotations and check runs detail
