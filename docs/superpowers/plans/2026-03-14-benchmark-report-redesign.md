# Benchmark Report HTML Redesign Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or
> superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite the `GenerateHtml` method in `tools/benchmark-report.cs` to produce a "Mission Control + Bento"
HTML dashboard instead of the current DaisyUI/Tailwind template.

**Architecture:** The method emits a single self-contained HTML file via `StringBuilder`. All styling is hand-crafted
CSS with custom properties for dark/light theming. Client-side JavaScript handles table sorting, pagination, chart
rendering, search, and keyboard navigation. The C# data serialization layer stays mostly the same; one new JSON blob
is added for measurement/percentile data.

**Tech Stack:** C# 14 raw string literals, Chart.js 4, Lucide icons, JetBrains Mono (all CDN). No Tailwind, no
DaisyUI.

**Spec:** `docs/superpowers/specs/2026-03-14-benchmark-report-redesign.md`
**Mockup:** `.superpowers/brainstorm/16211-1773449936/mission-bento-v2.html`

---

## Strategy

The `GenerateHtml` method (lines 440-1757) is a single C# method that appends a long HTML string with embedded
`{{expressions}}` for data injection. The rewrite replaces the entire HTML template while preserving the C# data
serialization at lines 440-462 (with one addition).

**Approach:** Replace the method in one pass. The HTML template is purely output; there are no intermediate C# logic
branches to preserve (the only C# expressions are `data.IsComparison` checks and `timestamp`). We build the new
template in sections, verifying each by running the tool against real benchmark data.

**Verification strategy:** After each task, build the tool and generate a report from a test fixture:

```sh
dotnet run --project tools/benchmark-report.cs -- \
  --input tools/tests/fixtures/ \
  --output /tmp/benchmark-report-test.html
```

Then open `/tmp/benchmark-report-test.html` in a browser to visually verify.

**Note:** If `tools/tests/fixtures/` does not contain BenchmarkDotNet JSON files, Task 0 creates a minimal fixture.
Alternatively, run benchmarks first:
`dotnet run --project test/Kurrent.Client.Tests/ -c Release -- --filter "*ReadStream*"` and use the generated
`BenchmarkDotNet.Artifacts/results/` directory.

**Dropped features:** The following features from the current DaisyUI template are intentionally removed in the
redesign: heatmap toggle, zoom controls, text-size adjustment, multi-theme selector (16+ DaisyUI themes), column
hover highlighting, and back-to-top FAB. These are not in the spec or mockup.

**File map:**

| File                          | Action | Responsibility                              |
| ----------------------------- | ------ | ------------------------------------------- |
| `tools/benchmark-report.cs`  | Modify | Rewrite `GenerateHtml` (lines 440-1757)     |
| `tools/tests/fixtures/`      | Create | Minimal BDN JSON fixture (if not present)   |

No other files are created or modified.

---

## Chunk 1: Foundation (CSS + Header + Environment)

### Task 0: Create test fixture (if needed)

Before any implementation, ensure a BenchmarkDotNet JSON fixture exists for verification.

**Files:**
- Create (if absent): `tools/tests/fixtures/` directory with at least one BDN JSON result file

- [ ] **Step 1: Check for existing fixtures**

```sh
ls tools/tests/fixtures/*.json 2>/dev/null || echo "No fixtures found"
```

If fixtures exist, skip to Task 1.

- [ ] **Step 2: Create a minimal fixture**

If no fixtures exist, create a minimal BenchmarkDotNet JSON file at `tools/tests/fixtures/minimal-report.json`.
The simplest approach is to copy the structure from a real BDN output. The JSON must have a `Benchmarks` array
with at least 3-4 entries, each containing `Statistics` (with `Mean`, `Median`, `StandardDeviation`,
`ConfidenceInterval`, `Percentiles`), `Memory`, and `Measurements` arrays.

Alternatively, run the existing benchmarks to generate real data:

```sh
dotnet test test/Kurrent.Client.Tests/ --framework net8.0 -- \
  --treenode-filter "/*/*/ReadStreamBenchmarks/*" --disable-logo
```

Then copy the JSON output from `test/Kurrent.Client.Tests/BenchmarkDotNet.Artifacts/results/`.

- [ ] **Step 3: Verify the fixture works with the current tool**

```sh
dotnet run --project tools/benchmark-report.cs -- \
  --input tools/tests/fixtures/ \
  --output /tmp/benchmark-report-test.html
```

Expected: HTML file generated without errors.

- [ ] **Step 4: Commit fixture**

```sh
git add tools/tests/fixtures/
git commit -m "test(benchmark-report): add BDN JSON fixture for report testing"
```

### Task 1: Add measurement/percentile JSON serialization

The spec requires sparkline and distribution data that is not on `BenchmarkEntry`. We need to serialize the raw
measurement values and percentile objects alongside the existing JSON blobs.

**Files:**
- Modify: `tools/benchmark-report.cs:440-462` (data serialization block)
- Modify: `tools/benchmark-report.cs:274-335` (ParseBenchmarkFiles, to capture raw data)
- Modify: `tools/benchmark-report.cs:129-137` (ReportJsonContext, add serializable type)

- [ ] **Step 1: Add MeasurementData record and update ParseBenchmarkFiles return type**

The current `ParseBenchmarkFiles` returns a 3-tuple:

```csharp
static (List<BenchmarkEntry>, BdnHostInfo?, JobConfig?) ParseBenchmarkFiles(List<string> jsonPaths)
```

Add the new record near the other model records (after line 76):

```csharp
record MeasurementData(
    double[] Values,
    JsonElement? Percentiles
);
```

Change the return type to a 4-tuple:

```csharp
static (List<BenchmarkEntry>, BdnHostInfo?, JobConfig?, Dictionary<string, MeasurementData>)
    ParseBenchmarkFiles(List<string> jsonPaths)
```

Inside the method, after building the `BenchmarkEntry` list, build the measurement map by iterating over the raw
`BdnBenchmark` objects. For each benchmark, extract `Measurements` where `IterationMode == "Workload"` and
`IterationStage == "Actual"`, collecting their `Value` doubles. Also grab `Statistics?.Percentiles`. Key the
dictionary by the same `CompositeKey` used for the `BenchmarkEntry`.

Update the return statement to include the new dictionary.

Update the call site in `Execute` (line ~185) to destructure the 4th element.

- [ ] **Step 2: Add JSON serialization for the measurement map**

Add to `ReportJsonContext` (around line 133):

```csharp
[JsonSerializable(typeof(Dictionary<string, MeasurementData>))]
```

In `GenerateHtml`, pass the measurement map as a parameter. After the existing serialization block (line 461), add:

```csharp
var measurementsJson = JsonSerializer.Serialize(measurementMap, Ctx.DictionaryStringMeasurementData);
```

The property name on the generated context will be `DictionaryStringMeasurementData` (source generator naming
convention: concatenation of type names). Verify by building; the compiler will error if the name is wrong.

Update the `GenerateHtml` signature to accept the measurement map:

```csharp
static string GenerateHtml(ReportData data, Dictionary<string, MeasurementData> measurementMap, double threshold)
```

- [ ] **Step 3: Verify the tool still builds and runs**

Run:

```sh
dotnet run --project tools/benchmark-report.cs -- --help
```

Expected: help output prints without errors.

- [ ] **Step 4: Commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): serialize measurement/percentile data for sparklines"
```

### Task 2: Replace HTML head and CSS with Mission Control design system

Replace the entire `<head>` section and embedded CSS. This is the foundation for all subsequent visual work.

**Files:**
- Modify: `tools/benchmark-report.cs:464-492` (head section)

- [ ] **Step 1: Replace the head section**

Replace from `<!DOCTYPE html>` through `</head>` with the new template. Key changes:

- Remove DaisyUI and Tailwind CDN links
- Add JetBrains Mono Google Fonts link
- Keep Lucide and Chart.js CDN links
- Replace the small inline CSS block with the full Mission Control CSS from the mockup (approximately 920 lines
  of CSS covering all components)

The CSS must include:
- `:root` dark theme tokens
- `[data-theme="light"]` light theme tokens
- All component styles: sticky header, env panel, bento cards, section headers, data table, charts disclosure,
  pagination, keyboard hints, legends, footer
- Entry animations (`fadeSlideUp`, `growRight`, `fadeIn`, `pulseGlow`)
- Responsive adjustments

Copy the complete `<style>` block from the mockup file
(`.superpowers/brainstorm/16211-1773449936/mission-bento-v2.html` lines 11-920), adapting only the closing
`</style></head>` to match the C# raw string context.

- [ ] **Step 2: Replace the body opening tag**

Change the body tag from the Tailwind-styled version:

```html
<body class="bg-base-100 text-base-content leading-relaxed max-w-[1400px] mx-auto">
```

To the plain version:

```html
<body>
```

(The `max-width`, `margin`, `padding` are now in the CSS body rule.)

- [ ] **Step 3: Build and verify**

```sh
dotnet run --project tools/benchmark-report.cs -- --help
```

Expected: builds cleanly. The HTML output will be broken visually (no body content matches the new CSS yet) but
the tool should not crash.

- [ ] **Step 4: Commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): replace head/CSS with Mission Control design system"
```

### Task 3: Replace sticky header

Replace the current DaisyUI sticky header with the Mission Control version.

**Files:**
- Modify: `tools/benchmark-report.cs:495-584` (sticky header HTML)

- [ ] **Step 1: Replace the sticky header HTML**

The new header structure (from mockup lines 925-953). Note: the `header-meta` is inline with the title, and
expand/collapse-all button is preserved:

```html
<div class="sticky-header">
  <div class="header-top">
    <div class="header-title">
      <h1>Kurrent.Client Benchmark Report</h1>
      <span class="badge badge-mode">{{(data.IsComparison ? "comparison" : "single-run")}}</span>
      <span class="badge badge-count" id="benchmark-count-badge"></span>
      <span class="header-meta">Generated {{timestamp}}</span>
    </div>
    <div class="header-controls">
      <div class="icon-btn" title="Toggle theme" onclick="toggleTheme()">
        <i data-lucide="sun-moon" style="width: 15px; height: 15px;"></i>
      </div>
      <div class="icon-btn" id="toggle-all" title="Expand/Collapse all" onclick="toggleAllSections()">
        <i data-lucide="unfold-vertical" style="width: 15px; height: 15px;"></i>
      </div>
    </div>
  </div>
  <div class="header-row-2">
    <div class="search-box">
      <input class="search-input" type="search" id="global-search"
             placeholder="Search benchmarks..." oninput="filterSearch(this.value)">
      <span class="kbd kbd-inside">/</span>
    </div>
    <select class="jump-select" id="jump-select" onchange="jumpToSection(this.value)">
      <option value="">Jump to...</option>
    </select>
  </div>
</div>
```

Note the C# interpolation expressions `{{(data.IsComparison ? ...)}}` and `{{timestamp}}` that must be preserved.

- [ ] **Step 2: Build and verify**

```sh
dotnet run --project tools/benchmark-report.cs -- --help
```

- [ ] **Step 3: Commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): replace sticky header with Mission Control design"
```

### Task 4: Replace environment panel and content container

Replace the content container div structure and the environment section placeholder.

**Files:**
- Modify: `tools/benchmark-report.cs:586-636` (content container, env, stats, sections, legends, footer)

- [ ] **Step 1: Replace the content body HTML**

Replace the old container divs (`#env-section`, `#stats-row`, `#regression-section`, `#class-sections`, legends,
footer) with the new structure:

```html
<!-- Environment Panel -->
<div class="env-panel" id="env-section"></div>

<!-- Bento Stat Cards -->
<div class="bento-grid" id="bento-stats"></div>

<!-- Regression Summary (comparison mode only) -->
<div id="regression-section" style="display:none;"></div>

<!-- Benchmark Sections (one per class) -->
<div id="class-sections"></div>

<!-- Keyboard hints -->
<div class="keyboard-hints">
  <span class="hint"><span class="kbd">j</span><span class="kbd">k</span> navigate rows</span>
  <span class="hint"><span class="kbd">/</span> search</span>
  <span class="hint"><span class="kbd">Enter</span> expand</span>
  <span class="hint"><span class="kbd">Esc</span> collapse</span>
</div>

<!-- Legends -->
<div class="legends-card" id="legends-section"></div>

<!-- Footer -->
<footer>
  <code>Kurrent.Client</code> &middot; Generated by benchmark-report
</footer>
```

Remove the old FAB back-to-top button (not in the new design).

- [ ] **Step 2: Build and verify**

```sh
dotnet run --project tools/benchmark-report.cs -- --help
```

- [ ] **Step 3: Commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): replace content container with Mission Control layout"
```

---

## Chunk 2: JavaScript Core (Rendering Functions)

### Task 5: Replace JavaScript globals and formatting functions

Replace the old JavaScript block opening (constants, formatting, color functions) with the Mission Control versions.

**Files:**
- Modify: `tools/benchmark-report.cs:638-769` (JS globals + formatting + bar colors + metrics)

- [ ] **Step 1: Replace JS global constants**

Keep the same 5 JSON injections plus add the new measurements blob:

```javascript
const benchmarks = {{benchmarkJson}};
const comparisons = {{comparisonJson}};
const env = {{envJson}};
const jobConfig = {{jobConfigJson}};
const classes = {{classesJson}};
const measurements = {{measurementsJson}};
const isComparison = {{(data.IsComparison ? "true" : "false")}};
const threshold = {{threshold}};
```

- [ ] **Step 2: Write formatting functions**

Port the following from the current JS, updating style to match new design (no DaisyUI badge classes):

- `formatNs(ns)` - unchanged logic
- `formatBytes(bytes)` - unchanged logic
- `formatOps(ops)` - unchanged logic
- `formatDelta(pct)` - unchanged logic
- `allocBadge(bytes)` - new CSS classes: `alloc-zero`, `alloc-low`, `alloc-mid`, `alloc-high`
- `rankBadge(rank)` - new CSS classes: `rank-1`, `rank-2`, `rank-3`, `rank-n`
- `benchLabel(b)` - unchanged logic
- `barColor(index, total)` - map rank to accent colors (green, blue, amber, red)

- [ ] **Step 3: Build and verify**

```sh
dotnet run --project tools/benchmark-report.cs -- --help
```

- [ ] **Step 4: Commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): replace JS globals and formatting with Mission Control style"
```

### Task 6: Write environment panel renderer

Replace the old `renderEnvSection` with the new Mission Control two-card layout.

**Files:**
- Modify: `tools/benchmark-report.cs:773-812` (env section JS)

- [ ] **Step 1: Write the new environment renderer**

Function `renderEnvPanel()` builds two cards in the `#env-section` div:

- **Machine card**: icon (monitor), fields: OS, CPU, Cores, Architecture
- **Configuration card**: icon (settings), fields: Runtime, BDN Version, Toolchain, Job (warmup/iterations/config)

Uses the `env` and `jobConfig` JSON blobs. Each card uses `.env-card` > `.env-card-title` > `.env-grid` structure.

- [ ] **Step 2: Build and verify**

Run the tool against real data and check the environment panel renders correctly.

- [ ] **Step 3: Commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): add Mission Control environment panel renderer"
```

### Task 7: Write bento stat cards renderer

New function that populates the `#bento-stats` grid with four aggregate metric cards.

**Files:**
- Modify: `tools/benchmark-report.cs` (add new JS function in script block)

- [ ] **Step 1: Write `renderBentoStats()` function**

Compute from the `benchmarks` array:

- **Fastest**: benchmark with lowest `MeanNs` -> green glow
- **Slowest**: benchmark with highest `MeanNs` -> red glow
- **Peak Alloc**: benchmark with highest `AllocatedBytes` -> amber glow
- **Throughput**: benchmark with highest `OpsPerSec` -> blue glow

Each card: `.bento-card` > `.bento-label` (icon + uppercase text) > `.bento-value` (large number) > `.bento-detail`
(method name).

Use `fadeSlideUp` animation with staggered `animation-delay` per card.

- [ ] **Step 2: Build and verify**

Run tool, check four bento cards render with correct data.

- [ ] **Step 3: Commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): add bento stat cards renderer"
```

---

## Chunk 3: Per-Class Sections (Table + Charts)

### Task 8: Write per-class section generator

Replace the old per-class section loop (lines 913-1137) with the new Mission Control section structure.

**Files:**
- Modify: `tools/benchmark-report.cs:913-1137` (class sections loop)

- [ ] **Step 1: Write the section header and table generator**

For each class in `classes`, generate:

1. **Section header**: `.section-header` with status dot, chevron, class name, count
2. **Group-by pills**: `.group-controls` with None/Category pills
3. **Data table**: `.table-wrap` > `<table>` with columns: Method, Count, Mean, Ops/s, Dist., Error, StdDev, P95,
   Alloc, (optional GC cols), Rank

Table headers use `.th-inner` flex wrapper with Lucide icon + text + sort arrow.

Each table row populates:

- Method: bold name + parameters subtitle
- Count: `Measurements` array length (from `measurements[compositeKey]`)
- Mean/Ops/StdDev/Error/P95: formatted values
- Dist.: sparkline histogram from measurement values
- Alloc: `allocBadge(bytes)` with threshold-based color
- GC cols: conditionally shown if any benchmark has Gen0/1/2 > 0
- Rank: `rankBadge(rank)`

- [ ] **Step 2: Add pagination below table**

Inside `.table-wrap`, add `.table-pagination#pagination-{sectionId}` div. Wire up the pagination function
per-section.

- [ ] **Step 3: Add charts disclosure below table**

After `.table-wrap`, add the collapsible charts disclosure:

```html
<div class="charts-disclosure" id="charts-{sectionId}">
  <div class="charts-toggle" onclick="toggleCharts('{sectionId}')">...</div>
  <div class="charts-body">
    <div class="charts-grid">
      <div class="chart-card"><!-- bar chart --></div>
      <div class="chart-card"><!-- distribution plot --></div>
    </div>
  </div>
</div>
```

The bar chart canvas: `<canvas id="barChart-{sectionId}">`.
The distribution plot: build CSS box-whisker rows from percentile data.

- [ ] **Step 4: Build and verify**

Run tool, verify sections render with table and charts.

- [ ] **Step 5: Commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): add per-class section generator with table and charts"
```

### Task 9: Write comparison mode extensions

Add the delta column and regression summary for comparison mode.

**Files:**
- Modify: `tools/benchmark-report.cs` (comparison-specific JS blocks)

- [ ] **Step 1: Add delta column to table when `isComparison`**

When `isComparison` is true, add two columns after the standard set:

- **Delta**: formatted percentage with color (green negative = improvement, red positive = regression)
- **Classification**: badge with "Regressed"/"Improved"/"Unchanged"/"New" text

Use the `comparisons` array, matching by `CompositeKey`.

- [ ] **Step 2: Write regression summary renderer**

Function `renderRegressionSummary()` populates `#regression-section` when regressions exist. Shows a bordered card
with a table of regressed benchmarks: name, baseline mean, current mean, delta %.

- [ ] **Step 3: Build and verify**

If baseline fixtures exist, test comparison mode. Otherwise verify the code compiles and the single-run path still
works.

- [ ] **Step 4: Commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): add comparison mode delta columns and regression summary"
```

---

## Chunk 4: Interactive Features (Sort, Search, Pagination, Charts)

### Task 10: Port table sorting

Port the sortable table logic from the mockup.

**Files:**
- Modify: `tools/benchmark-report.cs` (JS sorting block)

- [ ] **Step 1: Write table sorting handler**

Event delegation on `th[data-sort]` click:

- Toggle `sort-asc`/`sort-desc` classes
- Sort rows by cell text (numeric-aware parsing)
- Re-run pagination after sort
- Re-create Lucide icons

Copy from mockup lines 1607-1637, adapting to support multiple tables (one per section).

- [ ] **Step 2: Build and verify**

Run tool, click column headers to verify sorting works.

- [ ] **Step 3: Commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): add sortable table columns"
```

### Task 11: Port pagination

Port the pagination system from the mockup.

**Files:**
- Modify: `tools/benchmark-report.cs` (JS pagination block)

- [ ] **Step 1: Write pagination functions**

Port from mockup lines 1538-1604. Adapt to support multiple tables by accepting a section ID parameter. Each
section gets its own page state.

Functions: `paginate(sectionId)`, page button click handlers, page-size selector.

- [ ] **Step 2: Build and verify**

Run tool, verify pagination controls appear and work.

- [ ] **Step 3: Commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): add table pagination"
```

### Task 12: Port search, jump-to, and keyboard navigation

Port the interactive features from both the mockup and the current report.

**Files:**
- Modify: `tools/benchmark-report.cs` (JS interactive features)

- [ ] **Step 1: Write search filter**

Function `filterSearch(query)`: hide/show table rows matching method name. Update pagination after filter.

- [ ] **Step 2: Write jump-to dropdown**

Populate `#jump-select` with class names. On change, scroll to the section.

- [ ] **Step 3: Write keyboard navigation**

Key handlers:

- `j`/`k`: navigate table rows (add/remove `.active` class)
- `/`: focus search input
- `Enter`: expand current section
- `Esc`: collapse current section, blur search

- [ ] **Step 4: Build and verify**

Run tool, test search filtering, jump-to, and keyboard shortcuts.

- [ ] **Step 5: Commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): add search, jump-to, and keyboard navigation"
```

### Task 13: Port Chart.js bar chart with animation

Port the bar chart builder and custom animation from the mockup.

**Files:**
- Modify: `tools/benchmark-report.cs` (JS chart block)

- [ ] **Step 1: Write `buildBarChart(sectionId, metricKey)` function**

Creates a Chart.js horizontal bar chart for the given section and metric. Supports four metrics: Mean, Ops/s, P95,
Alloc. Supports log/linear scale toggle.

Chart config from mockup lines 1454-1535, adapted to use dynamic data from `benchmarks` filtered by class.

- [ ] **Step 2: Write `animateChart()` function**

Copy from mockup lines 1640-1667. Custom RAF loop with easeOutQuart and per-bar stagger.

- [ ] **Step 3: Write metric pill switching**

Copy from mockup lines 1674-1720. On pill click: update active state, reconfigure chart data/scale, replay
animation.

- [ ] **Step 4: Write `toggleCharts(sectionId)` function**

Toggle `.collapsed` class on `.charts-disclosure`.

- [ ] **Step 5: Build and verify**

Run tool, verify bar charts render and animate, metric pills switch correctly.

- [ ] **Step 6: Commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): add Chart.js bar charts with staggered animation"
```

---

## Chunk 5: Polish and Legends

### Task 14: Write legends renderer and theme toggle

Port the remaining UI components.

**Files:**
- Modify: `tools/benchmark-report.cs` (JS legends + theme)

- [ ] **Step 1: Write `renderLegends()` function**

Populates `#legends-section` with a two-column grid:

- Left: metric icons and descriptions (Mean, Ops/s, Error, StdDev, P95, Gen0/1/2)
- Right: time unit abbreviations (ns, us, ms, s)

- [ ] **Step 2: Write `toggleTheme()` function**

Switches `data-theme` between dark/light. Rebuilds all chart instances.

- [ ] **Step 3: Write section collapse/expand**

Function `toggleSection(sectionId)`: toggles visibility of the section body, rotates the chevron.

- [ ] **Step 4: Wire up initialization**

At the end of the script block, call all renderers:

```javascript
renderEnvPanel();
renderBentoStats();
renderRegressionSummary();
classes.forEach(cls => renderClassSection(cls));
renderLegends();
document.getElementById('benchmark-count-badge').textContent = benchmarks.length + ' benchmarks';
lucide.createIcons();
```

- [ ] **Step 5: Build and generate a full report**

```sh
dotnet run --project tools/benchmark-report.cs -- \
  --input tools/tests/fixtures/ \
  --output /tmp/benchmark-report-test.html
```

Open in browser. Verify all components render, theme toggle works, charts animate, tables sort and paginate.

- [ ] **Step 6: Commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): add legends, theme toggle, and initialization"
```

### Task 15: Final integration test and cleanup

Verify the complete report works end-to-end.

**Files:**
- Modify: `tools/benchmark-report.cs` (minor fixups only)

- [ ] **Step 1: Test single-run mode**

Generate report from test data, verify all sections render correctly.

- [ ] **Step 2: Test comparison mode (if baseline data available)**

```sh
dotnet run --project tools/benchmark-report.cs -- \
  --input tools/tests/fixtures/ \
  --baseline tools/tests/fixtures/ \
  --output /tmp/benchmark-report-comparison.html
```

Verify delta columns, regression summary, comparison badge.

- [ ] **Step 3: Test light theme**

Toggle theme, verify all elements have correct light-mode colors.

- [ ] **Step 4: Verify JSON summary output still works**

```sh
dotnet run --project tools/benchmark-report.cs -- \
  --input tools/tests/fixtures/ \
  --summary-json /tmp/benchmark-summary.json
```

Verify the JSON file is valid and contains expected fields.

- [ ] **Step 5: Remove any dead code from old template**

Scan for any leftover DaisyUI/Tailwind references, old CSS classes, or unused JS functions.

- [ ] **Step 6: Final commit**

```sh
git add tools/benchmark-report.cs
git commit -m "feat(benchmark-report): complete Mission Control + Bento HTML redesign

Replace DaisyUI/Tailwind HTML template with hand-crafted Mission Control
aesthetic. Adds JetBrains Mono, CSS custom property theming (dark+light),
bento stat cards, animated Chart.js bar charts, box-whisker distribution
plots, inline sparklines, sortable tables with pagination, and keyboard
navigation."
```
