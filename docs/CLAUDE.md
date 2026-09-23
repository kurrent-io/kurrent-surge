# Kurrent Surge docs site

> Astro + Starlight, built with Bun, published to GitHub Pages.

Same bar as the root `CLAUDE.md`: record only what breaks convention or cost a session. If a
senior developer can guess it from `astro.config.mjs`, leave it out.

## Read the docs before you touch anything

Use the Astro and Starlight documentation before, during, and after every change here. The optional
agent tooling is listed in the root `CONTRIBUTING.md`, under "Work on the docs site".

- The `astro-docs` MCP (`search_astro_docs`), when it is installed, for any API, config key, or
  default. Never answer from memory. Astro 7 and Starlight move fast, and the answers depend on the
  version.
- The source in `node_modules` when the docs are ambiguous. Read the integration's code.

Every expensive mistake below came from solving a problem from first principles when one
documentation lookup would have ended it. The `server.port` saga cost hours. The answer was one
sentence in the configuration reference.

## Working rules

Each rule cost real time.

- **Stop the dev server before any install.** `bun add`, `bun remove`, and `bun patch --commit`
  swap files under a running server. The process stays alive but bound to nothing. It looks up,
  answers nothing, and `astro dev status` still reports it as running. The second failure mode is
  worse because the site keeps working: Vite's pre-bundled deps under `node_modules/.vite` go
  stale, and only the modules reached through a dynamic `import()` fail, with
  `504 (Outdated Optimize Dep)` in the console. An install during a session took out the hero
  motion field alone — `import("three")` 504'd, the host reported `state: "error"`, and
  `MotionField.astro` removed the mount exactly as its no-WebGL fallback is designed to. Every
  other page looked perfect. Delete `node_modules/.vite` and restart.
- **Never delete `.astro/` under a running server.** Slug resolution breaks. The build fails with
  "the slug `x` does not exist" for a page that is right there on disk. Restart the server to fix
  it. `--force` clears the content-layer cache.
- **Verify UI in a browser and measure it.** A green build, a grep, or correct markup in `dist/` is
  not verification. Every probe returns a control value that proves it queried the right element.
  A probe that reports "missing" without showing what it looked at is a guess.
- **Starlight stores the System theme as `""`, never `"auto"`.** Its pre-paint script in
  `ThemeProvider.astro` reads `localStorage['starlight-theme']` and paints dark for any truthy
  value other than `"light"`. A footer that stored `"auto"` made every page start dark on a
  light OS. `Head.astro` migrates a stored `"auto"` to `""` before paint.
- **The landing page is a Starlight page.** `src/content/docs/index.mdx`, `template: splash`,
  composed from the three components under `src/components/home/` plus `MotionField`. It inherits
  the header, the footer, `customCss`, the `head` config and the per-page `og:image` like any
  other page. Its section furniture is `src/styles/home-v2.css`, imported by the page itself so it
  never loads on a docs page, and the components carry `:root:has(.home-v2)` rules that release
  the content-width cap and hide the docs-page footer chrome. The previous landing page lived
  outside the collection at `src/pages/index.astro` with its own `<head>`. Every global change had
  to be duplicated there by hand, and one font removal broke the build on that page alone. That
  file is gone. `src/styles/home.css` was its stylesheet and now has one importer, the
  `src/dev/motion-lab.astro` lab page.
- **The `logo` config option does nothing here.** It feeds only
  `virtual:starlight/user-images`, which is read by exactly one component — `SiteTitle` — and this
  site overrides it to colour "Surge". Setting `logo` would be a config entry nothing renders.
  `SiteTitle.astro` imports the mark directly, and carries Starlight's own `img` sizing rule
  because replacing the component drops it. `virtual:starlight/user-images` is declared in
  `virtual-internal.d.ts`, not the public `virtual.d.ts`, so importing it costs a `ts(2307)`.
- **`head` in the config is global.** A tag that differs per page cannot live there. Use
  `routeMiddleware` and push onto `starlightRoute.head`, which is the documented seam and is typed
  as "tags to include in the head of the current page". `src/routeData.ts` adds the per-page
  `og:image` that way. Do not hand-edit the `Head` override for this.

## Stack

| Concern | Choice | Note |
|---|---|---|
| Diagrams | `astro-d2`, rendered to SVG at build time | `experimental.useD2js` renders through WebAssembly. No `d2` binary is needed anywhere, CI included. |
| Fonts | Astro's native `fonts:` config | `fontProviders.fontsource()`. Never hand-roll `@font-face` fallbacks. Poppins and Fira Code use `display: 'optional'`: GitHub Pages caps font caching at `max-age=600` and `astro dev` serves fonts with `no-store`, so `swap` paints the fallback before the real face on revalidation. `optional` blocks up to 100 ms and never swaps, and when the face is not there at first render it keeps the fallback for the whole page view. OpenDyslexic is `block` for that reason: its fallback is Arial at the OpenDyslexic size adjustment, wrong face and wrong size for the one reader who asked for it, and under `no-store` a preload that finishes before the stylesheet asks is discarded and fetched again, so "there at first render" was a coin flip. `block` holds the text invisible until the file lands. `Head.astro` injects its preloads from `fontData` (`src.url`, never `experimental_getFontFileURL`, which returns the dev server's `[::1]` origin) only when the reader has it on, and the footer runs `document.fonts.load()` before flipping `data-font`. It runs 41% wider than Poppins at equal x-height, so the `[data-font="dyslexic"]` scope in `surge.css` sets `font-size-adjust: 0.397`, the width match: the switch no longer reflows a single line. Code is exempt. |
| Tailwind | v4 through `@tailwindcss/vite` and `@astrojs/starlight-tailwind`. `src/styles/global.css` is first in `customCss`. | Starlight's documented layering: theme and utilities only, no Preflight. The compatibility CSS defines the `dark` variant on `data-theme`. Do not redefine it. `surge.css` stays unlayered and wins. |
| UI frameworks | None | No React, no Svelte. Interactive controls are `.astro` components with their own scripts. |
| Code blocks | Expressive Code, configured only in `ec.config.mjs` | An `expressiveCode` key in `astro.config.mjs` makes Starlight ignore that file. |
| Icons | UnoCSS `presetIcons` with a safelist | Set in `src/expressive-code/block-header.mjs`. `starlight-plugin-icons` is not used. |
| Shortcut keys | `starlight-kbd` with `globalPicker: false` | The footer's OS picker is the Starwind `select` in `Footer.astro`. It drives the plugin's `sl-kbd-type` key and its `data-sl-kbd-active` contract. The plugin's own picker is not rendered. If you ever render it, import `{ KbdPicker } from 'starlight-kbd/components'`. The `./components/KbdPicker.astro` export points at a file that does not exist in 0.4.0. |
| Footer controls | Starwind `toggle-group`, `switch`, `select` | The aspire.dev `CustomSelect.astro` port was retired for the Starwind `select`. Its look was kept: the footer's own unlayered CSS styles the Starwind parts through their `data-slot` and class hooks. |
| Port | `server: { port: 4400 }` | Documented and sufficient. See "Dev server and the port". |

## Starwind UI

**Starlight first. Starwind only when Starlight lacks the component.** Content goes through
Starlight's own components: `Aside`, `Tabs`, `Card`, `LinkCard`, `Steps`, `FileTree`, `Badge`,
`Code`, `Icon`. Starwind fills the gaps Starlight leaves, which are the form controls and the
overlays: switch, toggle, select, dialog, tooltip, and the like. Do not rebuild a Starlight
component with Starwind, and do not hand-write a control Starwind already ships.

Starwind is a set of Astro-first components in the shadcn style, on a framework-neutral runtime.
It needs no React. Use the free tier only. Pro blocks need `STARWIND_LICENSE_KEY` and are never
added.

Installed in `docs/` on 2026-09-02 with `switch`, `toggle-group`, and `select`: the three
footer preference controls. The footer's own CSS still owns the look. Every rule in
`Footer.astro` is unlayered, so it beats the components' utility classes regardless of
specificity. A gallery of all 55 free styled components, rendered live, lives in the probe copy
under `.claude/context/.scratch/starwind-probe` at `/starwind/` when its preview runs.

- **MCP.** The `starwind-ui` MCP server is optional. `CONTRIBUTING.md` gives the install command. It has
  five tools: `starwind_search`, `starwind_docs`, `starwind_init`, `starwind_add`, and
  `starwind_migrate`. Each tool reads data or generates a command. None executes anything.
  `starwind_docs` allows about ten calls a minute.
- **Runner.** The CLI's `--package-manager` flag accepts `npm`, `pnpm`, or `yarn` only. In the
  probe, `npx starwind init` used npm and wrote `package-lock.json` without touching `bun.lock`.
  `bunx starwind add` detected bun and updated `bun.lock`. Always run `bunx starwind@latest …`.
  If the CLI leaves a `package-lock.json`, delete it and run `bun install`.
- **What `init` writes.** `starwind.config.json`, with components under
  `src/components/starwind`, utils under `src/lib/utils`, and the stylesheet at
  `src/styles/starwind.css`. It also writes that stylesheet, a `@/*` alias in `tsconfig.json`,
  `.vscode/starwind.code-snippets`, and these dependencies: `@starwind-ui/astro`,
  `@tabler/icons`, `@tailwindcss/forms`, `tailwind-merge`, `tailwind-variants`, and
  `tw-animate-css`. It reports "CSS import added to layout" and adds none. Wire the stylesheet by
  hand.
- **Fold the stylesheet, always.** The generated `starwind.css` opens with the full
  `@import "tailwindcss"`, which includes Preflight, then `@plugin "@tailwindcss/forms"`, then a
  `dark` variant on the `.dark` class. None of that can ship on a Starlight page. Keep the
  `@theme` and `@theme inline` blocks. Drop the three imports and the `:root` and `.dark`
  palettes. Map every `--color-*` directly onto a `surge.css` token. Then `@import
  './starwind.css'` from `global.css` after the Tailwind layer imports. Starwind's `--accent` is a
  hover surface and the `surge.css` `--accent` is the brand colour, so the mapping happens at the
  `--color-*` level and the intermediate variables are never defined. Remove
  `@tailwindcss/forms`: no styled component uses a `form-*` class.
- **What `add` writes.** Three files per component under `src/components/starwind/<name>/`:
  `<Name>.astro`, which wraps the primitive from `@starwind-ui/astro/<name>`, `variants.ts` for
  tailwind-variants, and `index.ts`. The source is yours to edit. `starwind update` can overwrite
  it, so preview with `--dry-run` or `--diff` first.
- **Persisted state before hydration.** A primitive reads `data-default-pressed`, and the other
  `data-default-*` attributes, once at init. To restore a saved choice, place an `is:inline`
  script after the element and set the attribute from `localStorage`. The runtime then hydrates
  in the pressed state. Verified on the toggle.
- **A single-select `toggle-group` releases on a second press.** The runtime proposes `[]`
  when the pressed item is pressed again. `starwind:value-change` is cancelable, so a
  `preventDefault()` on an empty value pins the choice. The footer theme group does this.
- **`select` portals its popup to `<body>` on open and repositions it on scroll.** Style the
  popup and items through global selectors on the classes passed to `SelectContent` and
  `SelectItem`; a scoped selector under the footer never matches. `--anchor-width` is set only
  while the popup is open, from the rounded trigger width.
- **The components need a reset, and the site has no Preflight.** Without it, every native
  `<button>` and `<input>` inside a component keeps the browser's grey `buttonface`, black text
  and Arial. Every Starwind element carries `data-slot`, so the folded stylesheet carries a
  Preflight subset scoped to `:where([data-slot], [data-slot] *)`: margin, padding, and border
  reset, headings and links inherit, lists unstyled, form controls inherit font and colour with a
  transparent background. Measured: 106 of 106 native controls in the gallery sit under a
  `data-slot` ancestor.
- **A copy of the site under `.claude/context/.scratch/` renders every component unstyled.**
  Tailwind v4's automatic source scan honours `.gitignore`, and that folder is ignored, so no
  component utilities are emitted. Add `@source '../components';` and `@source '../pages';`
  to `global.css` in such a copy. `docs/` itself is tracked and needs neither.

## Dev server and the port

**The dev server runs for the whole session, with hot reload. Not optional.** Start it before
the first edit: `bun run --cwd docs dev`. It serves `http://localhost:4400/kurrent-surge/`
and reloads `.astro`, CSS, and content on save. Sergio watches the site while edits land. A
session without it is a session he cannot see. `astro preview` serves the static `dist/` and
never reloads: use it only to verify a production build, then go back to `dev`. Stop the dev
server only for an install (see "Working rules"), and restart it right after.

**Fonts are cached in dev by our own Vite plugin.** Astro's dev font middleware sends
`Cache-Control: no-store` on every font file, unconditionally, so each navigation re-downloads
them. With `display: block` that is a blank flash of text on every click, and with `optional` it
is a coin flip whether the face is there at first render. `cacheFontsInDev()` in
`astro.config.mjs` (`enforce: 'pre'`, `apply: 'serve'`) rewrites the header to `immutable` for
`/_astro/fonts/` only; the URLs are content-hashed, so that is safe. Production is untouched.
Measured: after the first download, every navigation is a cache hit and the face is ready at the
first frame.

`server.port` in `astro.config.mjs` is the whole mechanism. It works.

The trap is background mode. When Astro detects an AI agent, it re-spawns itself detached and
writes `.astro/dev.json`. That child carries an explicit `--port 4321`. If `ASTRO_DEV_BACKGROUND=0`
does not take effect through a wrapper invocation, a stray server on 4321 holds the lock and the
next `astro dev` refuses to start. Hence:

```
"dev": "ASTRO_DEV_BACKGROUND=0 astro dev --force"
```

`ASTRO_DEV_BACKGROUND=0` keeps the server in the foreground. `--force` takes the lock back from a
stray. The scripts run plain `astro …` through `bun run`, the form Astro's own Bun recipe
documents. `bunx --bun` is not documented and did not take effect: the process ran under Node
regardless.

If a server appears on 4321 that you did not start, it is almost certainly one of your own earlier
invocations that slipped into background mode. Check `ps` for `--port 4321 --json` before you
blame anything else.

## D2 diagrams

- **Size is the whole game.** The reading column is 824px. A diagram wider than that is scaled
  down and its text shrinks with it. A much narrower one is inflated and becomes enormous. Pin the
  size with the `width=` fence attribute instead of fighting the layout.
- **`\n` does not break lines in D2 labels.** It concatenates. Keep labels short, or accept one
  line. Before you shorten a label, check the nearby prose and tables. Detail that is duplicated
  nearby can go. Detail that exists only in the diagram cannot.
- **A nested container's `direction` is ignored**, exactly as in mermaid. Only the top-level
  `direction` moves the layout.
- **`layout=elk`** packs ranks far tighter than the default dagre for deep vertical graphs, and
  looser for some others. Measure both.
- The palette lives in `surge.css`, keyed to D2's stable token classes such as `fill-N1` and
  `stroke-B1`. Only the per-diagram `.d2-<hash>` wrapper is content-hashed. The token classes
  survive regeneration. Colour belongs in `surge.css`, not in D2 source, or it stops following the
  theme toggle.

## The astro-d2 patch

`patches/astro-d2@0.13.1.patch` fixes a real upstream bug. The inline renderer tests for a
`d2version` property, but D2 emits `data-d2-version`, which hast names `dataD2Version`. So
`width`, `title`, and `data-*` were silently dropped. Without the patch, every `width=` in the docs
stops working with no error, and diagrams quietly resize.

The patch is pinned to the exact version, so a bump fails loudly instead of skipping it. It
belongs upstream at `joesaby/astro-d2`. The same bug is why animated, multi-board diagrams render
nothing at all.

## Type checking

`astro check` needs `@astrojs/check` and TypeScript 6.x. TypeScript 7's native compiler does not
expose the programmatic API that `astro check` relies on, and the command aborts with an explicit
message. Neither `astro dev` nor `astro build` type-checks anything, so `astro check` is the only
gate.

## CI

`.github/workflows/docs.yml` follows Astro's official GitHub Pages guide.

- The action detects `bun.lock`. It checks both `bun.lock` and `bun.lockb`. It then runs
  `bun install` and `bun run build`. No `build-cmd` override is needed.
- **Checkout uses `fetch-depth: 0`.** `lastUpdated: true` runs `git log` per page. A shallow clone
  stamps every page with the deploy commit's date, and nothing fails.
- The footer's `SHA` link reads `GITHUB_SHA` in Actions and `git rev-parse HEAD` locally.
  `astro.config.mjs` exposes the value as `import.meta.env.PUBLIC_COMMIT_SHA` through
  `vite.define`.
- **`patches/` must stay tracked.** `bun install` in CI cannot apply a patch that is not in the
  checkout, and the failure is silent in its effects.
- **GitHub Pages serves gzip, never brotli.** Verified against the live site and two other
  `github.io` hosts: asking for `br` alone returns the file uncompressed. Size decisions must be
  argued in gzip. Measured in brotli, bundling `three` looked 43 KB better than a CDN; in gzip the
  real figures are 127.9 KB bundled against 183.5 KB for full `three` from a CDN.
- **A `robots.txt` here would never be read.** The protocol is host-scoped, so crawlers fetch
  `https://kurrent-io.github.io/robots.txt` — a different repository. A file in `public/` deploys
  to `/kurrent-surge/robots.txt` and is ignored. Sites like aspire.dev have one because they own a
  root domain. Starlight already emits `<link rel="sitemap">`, which is the discovery route that
  works at a project-page base path.
- `base` is hardcoded to `/kurrent-surge/`, and every internal link under `src/` carries it,
  because Astro never rewrites authored links. Local and CI builds are identical. The dev server
  serves at `http://localhost:4400/kurrent-surge/`. `starlight-links-validator` fails the build on
  any link written without the prefix.
