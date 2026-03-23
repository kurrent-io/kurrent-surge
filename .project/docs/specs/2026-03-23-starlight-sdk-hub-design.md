# Kurrent SDK Developer Hub — Design Spec

**Date:** 2026-03-23
**Author:** Sergio + Claude
**Status:** Draft

## Overview

A new documentation and engineering hub for the Kurrent platform SDKs, built with Astro Starlight. Replaces
nothing — the existing VuePress site at `docs/` stays untouched. Content is ported (copied and adapted), not
migrated in-place.

The site serves two audiences: **SDK users** learning the API, and **the engineering team** publishing
postmortems, research, specs, and changelogs. The landing page is a developer hub that surfaces all content
types, inspired by Vercel Academy and Slack Engineering.

## Location

`.project/website/` inside the Kurrent.Client repository. Can be extracted to a standalone repo later if the
multi-SDK scope outgrows a single repo.

## Architecture

Three components:

1. **Astro Starlight** — SDK documentation, engineering docs, and changelogs. Provides sidebar navigation,
   Pagefind search, dark mode, TOC, and responsive layout.
2. **starlight-blog plugin** — Engineering blog with RSS, tags, author pages, and pagination. Separate content
   collection from the docs.
3. **Custom hub page** — `src/pages/index.astro` landing page that dynamically pulls latest blog posts,
   recent changelog entries, and featured documentation links.

Output is pure static HTML/CSS/JS. No SSR adapter. Deployable to GitHub Pages, Cloudflare Pages, or any
static host. Must also work locally via `pnpm dev`. Requires Node >= 18.17.1 (pin Node 22 LTS in CI).

### `base` path handling

Cloudflare Pages with a custom domain serves at `/` — no `base` config needed. GitHub Pages without a
custom domain serves at `https://<org>.github.io/Kurrent.Client/`, which requires `base: '/Kurrent.Client/'`.
To handle both targets, set `base` via environment variable:

```javascript
// astro.config.mjs
base: process.env.SITE_BASE || '/',
```

CI workflows set `SITE_BASE` per target. Local dev and Cloudflare use the default `/`.

## URL Structure

### SDK Documentation (`/dotnet/`)

Single-SDK now, multi-ready. The `/dotnet/` prefix reserves URL space for `/go/`, `/java/`, `/python/`,
`/rust/` when those SDKs exist. No SDK switcher built yet — YAGNI.

```
/dotnet/quickstart/
/dotnet/configuration/
/dotnet/error-handling/
/dotnet/serialization/
/dotnet/migration/
/dotnet/streams/appending/
/dotnet/streams/reading/
/dotnet/streams/managing/
/dotnet/processors/getting-started/
/dotnet/processors/checkpointing/
/dotnet/processors/error-handling/
/dotnet/registry/schemas/
/dotnet/registry/compatibility/
```

### Blog (`/blog/`)

```
/blog/                          # Post listing with pagination
/blog/<slug>/                   # Individual post
/blog/tags/                     # Tag index
/blog/tags/<tag>/               # Posts by tag
/blog/rss.xml                   # RSS feed
```

### Engineering (`/engineering/`)

Cross-cutting documentation that isn't specific to any SDK.

```
/engineering/postmortems/       # Listing + individual pages
/engineering/research/          # Listing + individual pages
/engineering/specs/             # Listing + individual pages
```

### Changelog (`/changelog/`)

Per-component, with auto-generated entries and optional hand-written editorial summaries.

```
/changelog/client/              # Kurrent.Client release history
/changelog/processors/          # Kurrent.Processors release history
```

### Landing Page

```
/                               # Developer hub (custom Astro page)
```

## Project Structure

```
.project/website/
├── astro.config.mjs
├── package.json
├── tsconfig.json
├── src/
│   ├── content/
│   │   ├── docs/
│   │   │   ├── dotnet/
│   │   │   │   ├── quickstart.md
│   │   │   │   ├── configuration.md
│   │   │   │   ├── error-handling.md
│   │   │   │   ├── serialization.md
│   │   │   │   ├── migration.md
│   │   │   │   ├── streams/
│   │   │   │   │   ├── appending.md
│   │   │   │   │   ├── reading.md
│   │   │   │   │   └── managing.md
│   │   │   │   ├── processors/
│   │   │   │   │   ├── getting-started.md
│   │   │   │   │   ├── checkpointing.md
│   │   │   │   │   └── error-handling.md
│   │   │   │   └── registry/
│   │   │   │       ├── schemas.md
│   │   │   │       └── compatibility.md
│   │   │   ├── engineering/
│   │   │   │   ├── postmortems/
│   │   │   │   │   └── 2026-03-ide-crash-loop.md
│   │   │   │   ├── research/
│   │   │   │   │   └── 2026-03-github-actions-dotnet-marketplace.md
│   │   │   │   └── specs/
│   │   │   │       ├── semver-bump.md
│   │   │   │       ├── benchmark-report.md
│   │   │   │       └── filter-expressions.md
│   │   │   └── changelog/
│   │   │       ├── client/
│   │   │       │   └── index.md
│   │   │       └── processors/
│   │   │           └── index.md
│   │   └── blog/
│   │       └── 2026-03-22-ide-crash-loop.md
│   ├── content.config.ts
│   ├── pages/
│   │   └── index.astro
│   ├── components/
│   │   └── Hub.astro
│   └── styles/
│       └── custom.css
├── public/
│   ├── fonts/
│   │   ├── Solina-Light.woff2
│   │   ├── Solina-Regular.woff2
│   │   ├── Solina-Medium.woff2
│   │   └── Solina-Bold.woff2
│   ├── favicon.ico
│   ├── favicon.png
│   ├── Kurrent Logo - Plum.svg
│   ├── Kurrent Logo - White.svg
│   └── Kurrent Logo - Black.svg
└── .gitignore
```

## Sidebar Configuration

```javascript
sidebar: [
  {
    label: '.NET SDK',
    items: [
      { slug: 'dotnet/quickstart' },
      { slug: 'dotnet/configuration' },
      { slug: 'dotnet/error-handling' },
      { slug: 'dotnet/serialization' },
      { slug: 'dotnet/migration' },
    ],
  },
  {
    label: 'Streams',
    autogenerate: { directory: 'dotnet/streams' },
  },
  {
    label: 'Processors',
    autogenerate: { directory: 'dotnet/processors' },
  },
  {
    label: 'Schema Registry',
    autogenerate: { directory: 'dotnet/registry' },
  },
  {
    label: 'Engineering',
    collapsed: true,
    items: [
      {
        label: 'Postmortems',
        autogenerate: { directory: 'engineering/postmortems' },
      },
      {
        label: 'Research',
        autogenerate: { directory: 'engineering/research' },
      },
      {
        label: 'Feature Specs',
        autogenerate: { directory: 'engineering/specs' },
      },
    ],
  },
  {
    label: 'Changelog',
    items: [
      { slug: 'changelog/client', label: 'Client' },
      { slug: 'changelog/processors', label: 'Processors' },
    ],
  },
]
```

## Theme & Branding

### Color mapping (Kurrent palette → Starlight CSS variables)

```css
/* Dark mode (primary) */
:root {
  --sl-color-accent-low: #2a0f1b;      /* Deep plum, muted */
  --sl-color-accent: #B75781;           /* Dusty Rose / secondary */
  --sl-color-accent-high: #DED9FF;      /* Pale Lavender */
  --sl-color-white: #f0f0f0;
  --sl-color-gray-1: #D9D9DE;           /* Neutral-200 */
  --sl-color-gray-2: #91939F;           /* Neutral-400 */
  --sl-color-gray-3: #6D6F7B;           /* Neutral-500 */
  --sl-color-gray-4: #4D4F57;           /* Neutral-700 */
  --sl-color-gray-5: #3A3B40;           /* Neutral-900 */
  --sl-color-gray-6: #26262B;           /* Neutral-950 */
  --sl-color-black: #080b0d;            /* Brand dark background */
}

/* Light mode — gray scale must be monotonic (1=lightest, 6=darkest) */
:root[data-theme='light'] {
  --sl-color-accent-low: #f5eef1;       /* Very light plum tint */
  --sl-color-accent: #631B3A;           /* Plum (primary) */
  --sl-color-accent-high: #3d1024;      /* Dark plum for contrast */
  --sl-color-white: #ffffff;
  --sl-color-gray-1: #F7F7F8;           /* Neutral-50  — lightest */
  --sl-color-gray-2: #EEEEF0;           /* Neutral-100 */
  --sl-color-gray-3: #D9D9DE;           /* Neutral-200 */
  --sl-color-gray-4: #B8B9C1;           /* Neutral-300 */
  --sl-color-gray-5: #6D6F7B;           /* Neutral-500 */
  --sl-color-gray-6: #42434A;           /* Neutral-800 — darkest */
  --sl-color-black: #000000;
}
```

### Typography

```css
:root {
  --sl-font: 'Solina', -apple-system, BlinkMacSystemFont, 'Segoe UI',
             Roboto, 'Helvetica Neue', Arial, sans-serif;
  --sl-font-mono: ui-monospace, 'SFMono-Regular', 'SF Mono', Menlo,
                  Consolas, 'Liberation Mono', monospace;
}
```

Solina self-hosted via `@font-face` in `custom.css` (4 weights: Light 400, Regular 450, Medium 500,
Bold 700). System mono stack for code — pragmatic choice over brand-specified Spline Sans Mono.

### Syntax highlighting

Shiki with `one-light` / `one-dark-pro` themes, matching the existing VuePress site.

### Accessibility

Pragmatic WCAG AA. Body text and interactive controls must hit 4.5:1 contrast ratio. Decorative elements
(hero gradients, accent backgrounds) get more freedom. The Plum-on-white combination (#631B3A on #fff)
achieves ~9.5:1 — well above AA.

## Content Migration Notes

### From VuePress (`docs/api/`)

Port all 13 markdown pages (5 root-level + 3 streams + 3 processors + 2 registry). Changes needed per file:

- Remove VuePress-specific frontmatter (`order:` → Starlight `sidebar.order:`)
- Convert `@[code{lines}{region}]()` imports to Starlight code blocks (inline the relevant snippets or
  use Astro's `import.meta.glob` for file imports)
- Convert `::: tip` / `::: warning` to Starlight's `:::note` / `:::caution` syntax
- Convert code tabs from vuepress-theme-hope format to Starlight `<Tabs>` / `<TabItem>` components
  (requires `.mdx` extension for pages using tabs)
- Update internal links from `./page.md` format to URL paths (`../page/`)
- Port definition list usage (install `remark-definition-list` or use HTML `<dl>` directly)

### From `.project/docs/`

Port directly. These are already clean markdown. Minor frontmatter additions needed (title, description,
sidebar metadata).

### Blog content

The IDE crash loop blog post (`.project/docs/postmortem/2026-03-ide-crash-loop/blog-post.md`) becomes the
first blog entry. Add starlight-blog frontmatter (date, authors, tags).

## Plugins

| Plugin                      | Purpose                                         |
|-----------------------------|-------------------------------------------------|
| `@astrojs/starlight`        | Core documentation framework                    |
| `starlight-blog`            | Blog with RSS, tags, pagination                 |
| `starlight-links-validator` | Catch broken internal links at build time        |
| `@astrojs/mdx` (via Starlight) | Component imports in docs — bundled with Starlight, do NOT add separately |
| `rehype-mermaid`            | Mermaid diagram rendering                        |
| `remark-definition-list`    | Definition list markdown syntax (if needed)      |

## Hub Landing Page

Custom `src/pages/index.astro` page that:

1. Queries the blog content collection for the 3 most recent posts
2. Shows featured SDK documentation links (hardcoded cards for Streams, Processors, Registry)
3. Displays recent changelog entries
4. Lists available SDKs (.NET active, others "coming soon")
5. Uses the Kurrent brand gradient for the hero section
6. Wraps in `StarlightPage` to get header, search, and dark mode toggle

## Build & Deploy

### Local development

```bash
cd .project/website
pnpm install
pnpm dev          # http://localhost:4321
```

### Build

```bash
pnpm build        # Output: dist/
pnpm preview      # Preview built site (search works here, not in dev)
```

### GitHub Actions

A workflow at the repo root `.github/workflows/deploy-docs.yml` (GitHub only reads workflows from the
repo-root `.github/workflows/`). Uses `defaults.run.working-directory: .project/website`.

1. Installs pnpm + Node 22 (Astro 5+ requires Node >=18.17.1; pinning 22 LTS for forward compat)
2. Runs `pnpm build` in `.project/website/`
3. Deploys `dist/` to GitHub Pages

### Cloudflare Pages

Connect repo → build command: `cd .project/website && pnpm install && pnpm build` → output: `.project/website/dist`

## What's NOT in scope

- Multi-SDK switcher UI (reserved URL space is enough for now)
- i18n / translations
- Versioned documentation (single "latest" version)
- Custom search beyond Pagefind defaults
- Analytics (PostHog/Segment can be added later via `head` config)
- CI/CD pipeline (design only — implementation is a separate plan)
- Comment system on blog posts

## Open Questions

None — all major decisions resolved during brainstorming.
