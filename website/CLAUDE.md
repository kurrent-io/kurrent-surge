# Kurrent Surge docs site

> Astro + Starlight, built with Bun, published to GitHub Pages.

Same bar as the root `CLAUDE.md`: only what breaks convention or cost a session. If a senior
developer would guess it from `astro.config.mjs`, it does not belong here.

## Read the docs first, every time

Before, during, and after touching anything here, use the Astro/Starlight tooling:

- The `astro-docs` MCP (`search_astro_docs`) for any API, config key, or default. Never answer
  from memory — Astro 7 and Starlight move fast and the answers are version-sensitive.
- The `astro-dev` skills (`astro-dev:lookup`, `astro-dev:astro-coding`) for patterns and the
  critical-rules checklist.
- `node_modules` is authoritative when docs are ambiguous. Read the integration's source.

Every expensive mistake below was a case of solving from first principles when one documentation
lookup would have ended it. The `server.port` saga cost hours and the answer was one sentence in
the configuration reference.

## Working rules

Earned the hard way. Each one cost real time.

- **Stop the dev server before any install.** `bun add`, `bun remove`, and `bun patch --commit`
  swap files under a running server and leave the process alive but bound to nothing — it looks
  up, answers nothing, and `astro dev status` still reports it running.
- **Never delete `.astro/` under a running server.** It breaks slug resolution: the build fails
  with "the slug `x` does not exist" for a page that is right there on disk. Restart to fix;
  `--force` clears the content-layer cache.
- **Verify UI in a browser and measure it.** A green build, a grep, or correct markup in `dist/`
  is not verification. Every probe returns a control value proving it queried the right element —
  a probe that reports "missing" without showing what it looked at is a guess.
- **The landing page is not a Starlight page.** `src/pages/index.astro` sits outside the content
  collection with its own `<head>`, its own stylesheets, and a hardcoded dark theme. Anything
  global — fonts, tokens, stylesheets — must be applied there too. Removing font packages broke
  the build on that one page while all 51 Starlight pages were fine.

## Stack

| Concern | Choice | Note |
|---|---|---|
| Diagrams | `astro-d2` → SVG at build time | `experimental.useD2js` renders through WebAssembly, so no `d2` binary anywhere, CI included |
| Fonts | Astro's native `fonts:` config | `fontProviders.fontsource()`; never hand-roll `@font-face` fallbacks |
| Tailwind | v4 through `@tailwindcss/vite` + `@astrojs/starlight-tailwind`, `src/styles/global.css` first in `customCss` | Starlight's documented layering: theme + utilities only, no Preflight. Its compatibility CSS defines the `dark` variant on `data-theme`; do not redefine it. surge.css stays unlayered and wins |
| UI frameworks | none | No React, no Svelte. Interactive controls are `.astro` components with their own scripts |
| Code blocks | Expressive Code, configured **only** in `ec.config.mjs` | An `expressiveCode` key in `astro.config.mjs` makes Starlight ignore that file |
| Icons | UnoCSS `presetIcons` + safelist | From `src/expressive-code/block-header.mjs`; `starlight-plugin-icons` is not used |
| Shortcut keys | `starlight-kbd`, `globalPicker: false` | The footer's OS picker is `CustomSelect.astro` (ported from aspire.dev, MIT) driving the plugin's `sl-kbd-type` key and `data-sl-kbd-active` contract. The plugin's own picker is not rendered; if it ever is, import `{ KbdPicker } from 'starlight-kbd/components'`, because the `./components/KbdPicker.astro` export points at a file that does not exist in 0.4.0 |
| Footer dropdowns | `CustomSelect.astro`, no shadcn, no React | Aspire's footer has no shadcn either; this is their component. A `popover="manual"` listbox over a hidden native `<select>`, which stays the value carrier and fires `change` |
| Port | `server: { port: 4400 }` | Documented and sufficient — see below |

## Dev server and the port

`server.port` in `astro.config.mjs` is the whole mechanism. It works.

The trap is background mode. When Astro detects an AI agent it re-spawns itself detached, writes
`.astro/dev.json`, and that child carries an explicit `--port 4321`. If `ASTRO_DEV_BACKGROUND=0`
fails to take effect through a wrapper invocation, you get a stray server on 4321 holding the lock,
and the next `astro dev` refuses to start against it. Hence:

```
"dev": "ASTRO_DEV_BACKGROUND=0 astro dev --force"
```

`ASTRO_DEV_BACKGROUND=0` keeps it in the foreground; `--force` takes the lock back from a stray.
Scripts use plain `astro …` run through `bun run`, which is the form Astro's own Bun recipe
documents — `bunx --bun` is not documented and did not take effect anyway (the process ran under
Node regardless).

If a server appears on 4321 that you did not start, it is almost certainly a previous invocation
of yours that slipped into background mode. Check `ps` for `--port 4321 --json` before blaming
anything else.

## D2 diagrams

- **Size is the whole game.** The reading column is 824px. A diagram whose natural width exceeds
  it is scaled down and its text shrinks with it; one much narrower is inflated and becomes
  enormous. Pin it with the `width=` fence attribute rather than fighting the layout.
- **`\n` does not break lines in D2 labels.** It concatenates. Keep labels short, or accept one
  line. When shortening a label, check the surrounding prose and tables first — detail duplicated
  nearby can go, detail that exists only in the diagram cannot.
- **Nested container `direction` is ignored**, exactly as in mermaid. Only the top-level
  `direction` moves the layout.
- **`layout=elk`** packs ranks far tighter than the default dagre for deep vertical graphs, and
  looser for some others. Measure both.
- The palette lives in `surge.css` keyed to D2's stable token classes (`fill-N1`, `stroke-B1`, …).
  Only the per-diagram `.d2-<hash>` wrapper is content-hashed; the token classes survive
  regeneration. Colour belongs there, not in D2 source, or it stops following the theme toggle.

## The astro-d2 patch

`patches/astro-d2@0.13.1.patch` fixes a real upstream bug: the inline renderer tests for a
`d2version` property, but D2 emits `data-d2-version` (hast: `dataD2Version`), so `width`, `title`
and `data-*` were silently dropped. Without it, every `width=` in the docs stops working with no
error and diagrams quietly resize.

Pinned to the exact version, so a bump fails loudly rather than skipping. It belongs upstream at
`joesaby/astro-d2` — the same bug is why animated (multi-board) diagrams render nothing at all.

## Type checking

`astro check` needs `@astrojs/check` **and TypeScript 6.x**. TypeScript 7's native compiler does
not expose the programmatic API it relies on and the command aborts with an explicit message.
Neither `astro dev` nor `astro build` type-checks anything, so this is the only gate.

## CI

`.github/workflows/docs.yml` follows Astro's official GitHub Pages guide.

- The action detects `bun.lock` (it checks both `bun.lock` and `bun.lockb`) and runs
  `bun install` then `bun run build`. No `build-cmd` override is needed.
- **Checkout uses `fetch-depth: 0`.** `lastUpdated: true` runs `git log` per page; a shallow clone
  stamps every page with the deploy commit's date and nothing fails.
- The footer's `SHA` link reads `GITHUB_SHA` in Actions and `git rev-parse HEAD` locally, exposed as
  `import.meta.env.PUBLIC_COMMIT_SHA` through `vite.define` in `astro.config.mjs`.
- **`patches/` must stay tracked.** `bun install` in CI cannot apply a patch that is not in the
  checkout, and the failure is silent in its effects.
- `base` is hardcoded to `/kurrent-surge/` and every internal link under `src/` carries it, because
  Astro never rewrites authored links. Local and CI builds are identical; the dev server serves at
  `http://localhost:4400/kurrent-surge/`, and `starlight-links-validator` fails the build on any
  link written without the prefix.
