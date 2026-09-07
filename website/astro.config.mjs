// @ts-check
import { defineConfig, fontProviders } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightLlmsTxt from 'starlight-llms-txt';
import starlightLinksValidator from 'starlight-links-validator';
import starlightGitHubAlerts from 'starlight-github-alerts';
import starlightScrollToTop from 'starlight-scroll-to-top';
import starlightPageActions from 'starlight-page-actions';
import starlightKbd from 'starlight-kbd';
import astroD2 from 'astro-d2';
import UnoCSS from 'unocss/astro';
import { execSync } from 'node:child_process';
import tailwindcss from '@tailwindcss/vite';

// GitHub Pages serves a project site from /<repo>/. Astro never rewrites authored
// links, so every internal link under src/ carries this prefix: changing the base
// means rewriting them.
const base = '/kurrent-surge/';

// Starlight's sitemap integration needs an absolute origin and silently skips without
// one. Same env-var treatment so a deploy target sets both together.
const site = process.env.SITE_URL ?? 'https://kurrent-io.github.io';

// Stamped into the footer as a link to the commit that built the site. Actions
// exposes it as GITHUB_SHA; a local build asks git.
const commit =
  process.env.GITHUB_SHA ?? execSync('git rev-parse HEAD', { encoding: 'utf8' }).trim();

// Dev server only. Astro serves every font file with `Cache-Control: no-store`
// (node_modules/astro/dist/assets/fonts/core/font-file-middleware.js, line 35,
// unconditional, same on upstream main), so each navigation re-downloads them
// and a `display: block` face hides its text for the length of the fetch. The
// dev URLs carry a content hash, so long caching is safe: a changed font gets
// a new URL. Production is untouched: the build copies the files into
// `_astro/fonts` and the host sets the headers. Remove when Astro caches them.
/** @returns {import('vite').Plugin} */
function cacheFontsInDev() {
  const fontPath = /\/_astro\/fonts\//;
  const dropped = /^(pragma|expires)$/i;
  return {
    name: 'surge:cache-fonts-in-dev',
    apply: 'serve',
    // Astro's own plugins carry no `enforce`, so `pre` sorts this one ahead of
    // them and its middleware is registered before Astro's font handler, which
    // ends the response. Registered after it, this never runs.
    enforce: 'pre',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        if (fontPath.test(req.url ?? '')) {
          const setHeader = res.setHeader.bind(res);
          res.setHeader = (name, value) => {
            if (dropped.test(String(name))) return res;
            if (/^cache-control$/i.test(String(name))) value = 'public, max-age=31536000, immutable';
            return setHeader(name, value);
          };
        }
        next();
      });
    },
  };
}

export default defineConfig({
  site,
  base,
  server: { port: 4400 },
  vite: {
    define: { 'import.meta.env.PUBLIC_COMMIT_SHA': JSON.stringify(commit) },
    plugins: [tailwindcss(), cacheFontsInDev()],
  },
  // Astro downloads these at build time and emits only the declared weights and
  // subsets. It also generates a metric-matched fallback per family from the
  // last generic in `fallbacks` (`optimizedFallbacks` is on by default), which is
  // what stops text re-flowing when the real face swaps in.
  fonts: [
    {
      provider: fontProviders.fontsource(),
      name: 'Poppins',
      cssVariable: '--font-poppins',
      weights: [300, 400, 500, 600, 700],
      // Body copy carries markdown emphasis across 20 pages, plus two italic
      // rules in home.css, so the real italics are worth their weight here.
      styles: ['normal', 'italic'],
      fallbacks: ['sans-serif'],
      // 100 ms block period, then the fallback stays for that page view: the
      // real face never swaps in over painted text. The hosts force this.
      // GitHub Pages sends max-age=600 on every font file, so after ten minutes
      // each navigation revalidates (~40 ms round trip), and `astro dev` sends
      // no-store, so every navigation refetches. With `swap` both paint the
      // fallback first. https://web.dev/articles/font-best-practices
      display: 'optional',
    },
    {
      provider: fontProviders.fontsource(),
      name: 'Fira Code',
      cssVariable: '--font-fira-code',
      weights: ['300 700'],
      // `styles` defaults to normal + italic. Nothing renders italic code, so
      // the italic files would ship unused.
      styles: ['normal'],
      fallbacks: ['monospace'],
      // Same reasoning as Poppins above.
      display: 'optional',
    },
    {
      // Reader-selectable alternative; see the [data-font] scope in surge.css.
      provider: fontProviders.fontsource(),
      name: 'OpenDyslexic',
      cssVariable: '--font-opendyslexic',
      weights: [400, 700],
      styles: ['normal'],
      fallbacks: ['sans-serif'],
      // Not `optional`: when this face misses first render, `optional` keeps
      // the fallback for the whole page view, and the fallback is Arial drawn
      // at the OpenDyslexic size adjustment, wrong face and wrong size for the
      // one reader who asked for it. `block` holds the text invisible until the
      // file lands, up to 3 s, and never paints a second face over a first.
      // Head.astro preloads it for readers who turned it on.
      display: 'block',
    },
  ],
  integrations: [
    // Resolves the icon class the code-block header plugin emits.
    UnoCSS({ injectReset: false }),
    // Renders `d2` fences to inline SVG at build time into public/d2/.
    //
    // `inline` puts the SVG in the DOM so surge.css can paint it from the theme
    // tokens; as an <img> it would be an isolated document and unreachable.
    // `theme.dark: false` stops D2 emitting its own `prefers-color-scheme` block,
    // which keys off the reader's OS and so desyncs from the site's theme toggle.
    // `useD2js` renders through the bundled WebAssembly build rather than a local
    // `d2` binary, so no build machine needs one installed.
    astroD2({
      inline: true,
      pad: 20,
      theme: { dark: false },
      experimental: { useD2js: true },
    }),
    starlight({
      title: 'Kurrent Surge',
      description: 'The .NET SDK for the Kurrent Platform.',
      // Expressive Code is configured entirely in ec.config.mjs. Declaring an
      // `expressiveCode` key here would make Starlight skip that file.
      components: {
        // The `title` option takes no markup, so colouring "Surge" alone needs
        // the component override.
        SiteTitle: './src/components/SiteTitle.astro',
        // Adds the filter box, the collapse toggle and the repository link
        // around Starlight's navigation list.
        Sidebar: './src/components/Sidebar.astro',
        SocialIcons: './src/components/SocialIcons.astro',
        // Community links, reader preferences and the build stamp under
        // Starlight's own edit-link / last-updated / pagination row.
        Footer: './src/components/Footer.astro',
        // Renders the breadcrumb trail above the page body.
        MarkdownContent: './src/components/MarkdownContent.astro',
        // Shipped by starlight-page-actions; it owns the Copy / Open / Share row.
        PageTitle: 'starlight-page-actions/overrides/PageTitle.astro',
        // Adds the typeface picker beside Starlight's theme picker, and the
        // pre-paint script that restores the reader's choice.
        ThemeSelect: './src/components/ThemeSelect.astro',
        Head: './src/components/Head.astro',
      },
      // Tailwind first, so its layers are declared before surge.css, which stays
      // unlayered and wins. Typefaces are declared in the top-level `fonts` option.
      customCss: ['./src/styles/global.css', './src/styles/surge.css'],
      editLink: { baseUrl: 'https://github.com/kurrent-io/kurrent-surge/edit/main/website/' },
      // Dates come from `git log` per page, so CI checks out the full history.
      lastUpdated: true,
      social: [
        {
          icon: 'github',
          label: 'GitHub',
          href: 'https://github.com/kurrent-io/kurrent-surge',
        },
      ],
      // Native grouped sidebar, not starlight-sidebar-topics. At 16 pages one
      // list is scannable, and a topic switcher would hide three quarters of
      // the site behind a control. Revisit topics past roughly 40 pages, or
      // when a genuinely separate audience appears — a CLI reference, or
      // versioned docs.
      //
      // The numbered labels are plain strings. starlight-theme.css already
      // renders group labels as mono uppercase eyebrows.
      sidebar: [
        {
          label: '01 · INTRODUCTION',
          items: [
            { label: 'What is Kurrent Surge', slug: 'what-is-surge' },
            { label: 'Getting Started', slug: 'getting-started' },
          ],
        },
        {
          label: '02 · CONFIGURATION',
          collapsed: true,
          items: [
            { label: 'Wiring Up Surge', slug: 'client/dependency-injection' },
            { label: 'Configuring the Client', slug: 'client/configuring-the-client' },
            { label: 'Security', slug: 'client/security' },
            { label: 'Clusters and Load Balancing', slug: 'client/clusters-and-discovery' },
            { label: 'Connection String Reference', slug: 'client/connection-strings' },
            { label: 'Troubleshooting', slug: 'client/troubleshooting' },
          ],
        },
        {
          label: '03 · STREAMS',
          collapsed: true,
          items: [
            { label: 'Overview', slug: 'streams' },
            { label: 'Append', slug: 'streams/append' },
            { label: 'Read', slug: 'streams/read' },
            { label: 'Manage', slug: 'streams/manage' },
            { label: 'Filters', slug: 'streams/filters' },
            { label: 'Record Metadata', slug: 'streams/metadata' },
            { label: 'Indexes', slug: 'client/indexes' },
          ],
        },
        {
          label: '04 · SCHEMA',
          collapsed: true,
          items: [
            { label: 'Overview', slug: 'schema' },
            { label: 'Catalogs', slug: 'schema/catalogs' },
            { label: 'Schema Names', slug: 'schema/names' },
            { label: 'Data Formats', slug: 'schema/formats' },
            { label: 'Registration', slug: 'schema/registration' },
            { label: 'Evolution', slug: 'schema/evolution' },
            { label: 'Registry', slug: 'client/registry' },
          ],
        },
        {
          label: '05 · PROCESSORS',
          collapsed: true,
          items: [
            { label: 'Overview', slug: 'processors' },
            { label: 'Writing a Processor', slug: 'processors/writing-a-processor' },
            { label: 'Checkpointing', slug: 'processors/checkpointing' },
            { label: 'Batching', slug: 'processors/batching' },
            { label: 'Error Handling', slug: 'processors/error-handling' },
            { label: 'Observability', slug: 'processors/observability' },
          ],
        },
        {
          label: '06 · CONNECTORS',
          collapsed: true,
          items: [
            { label: 'Overview', slug: 'client/connectors' },
            { label: 'Managing Connectors', slug: 'client/connectors/manage' },
            { label: 'Features', slug: 'client/connectors/features' },
            { label: 'Configure Connector', slug: 'client/connectors/settings' },
            { label: 'Metrics', slug: 'client/connectors/metrics' },
            {
              label: 'Sinks',
              collapsed: true,
              items: [
                { label: 'Elasticsearch', slug: 'client/connectors/sinks/elasticsearch' },
                { label: 'HTTP', slug: 'client/connectors/sinks/http' },
                { label: 'Kafka', slug: 'client/connectors/sinks/kafka' },
                { label: 'MongoDB', slug: 'client/connectors/sinks/mongo' },
                { label: 'Pulsar', slug: 'client/connectors/sinks/pulsar' },
                { label: 'RabbitMQ', slug: 'client/connectors/sinks/rabbitmq' },
                { label: 'Serilog', slug: 'client/connectors/sinks/serilog' },
                { label: 'SQL', slug: 'client/connectors/sinks/sql' },
              ],
            },
            {
              label: 'Sources',
              collapsed: true,
              items: [
                { label: 'Kafka', slug: 'client/connectors/sources/kafka' },
                { label: 'Webhook', slug: 'client/connectors/sources/webhook' },
              ],
            },
          ],
        },
        {
          label: '07 · ADMIN',
          collapsed: true,
          items: [
            { label: 'Admin', slug: 'client/admin' },
          ],
        },
        {
          label: '08 · MIGRATION',
          collapsed: true,
          items: [
            { label: 'Overview', slug: 'migration' },
            { label: 'From EventStoreDB', slug: 'migration/from-eventstoredb' },
            { label: 'From KurrentDB v1', slug: 'migration/from-kurrentdb-v1' },
            { label: 'Breaking Changes', slug: 'migration/breaking-changes' },
          ],
        },
      ],
      plugins: [
        // Emits /llms.txt so a coding agent can consume these docs directly.
        starlightLlmsTxt(),
        // Fails the build on a broken internal link. Cheap insurance while the
        // page set is still moving.
        starlightLinksValidator(),
        // Copy the page as Markdown, or hand it to an agent.
        starlightPageActions({
          share: true,
          actions: { chatgpt: true, claude: true, githubCopilot: true },
        }),
        // Renders GitHub's `> [!NOTE]` blockquote syntax as Starlight asides.
        starlightGitHubAlerts(),
        starlightScrollToTop({ showTooltip: true, tooltipText: { en: 'Scroll to top' } }),
        // Renders `<Kbd>` shortcuts for the reader's OS. The picker sits in the
        // footer preferences, so the header slot stays off.
        starlightKbd({
          globalPicker: false,
          types: [
            { id: 'mac', label: 'macOS', detector: 'apple' },
            { id: 'windows', label: 'Windows', detector: 'windows', default: true },
            { id: 'linux', label: 'Linux', detector: 'linux' },
          ],
        }),
      ],
    }),
  ],
});
