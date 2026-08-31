// @ts-check
import { defineConfig, fontProviders } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightLlmsTxt from 'starlight-llms-txt';
import starlightLinksValidator from 'starlight-links-validator';
import starlightGitHubAlerts from 'starlight-github-alerts';
import starlightScrollToTop from 'starlight-scroll-to-top';
import starlightPageActions from 'starlight-page-actions';
import astroD2 from 'astro-d2';
import UnoCSS from 'unocss/astro';

// GitHub Pages serves from /<repo>/ while Cloudflare serves from /. Setting the base
// through an env var keeps every internal link correct on both without a code change.
const base = process.env.SITE_BASE ?? '/';

// Starlight's sitemap integration needs an absolute origin and silently skips without
// one. Same env-var treatment so a deploy target sets both together.
const site = process.env.SITE_URL ?? 'https://kurrent-io.github.io';

export default defineConfig({
  site,
  base,
  server: { port: 4400 },
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
    },
    {
      // Reader-selectable alternative; see the [data-font] scope in surge.css.
      provider: fontProviders.fontsource(),
      name: 'OpenDyslexic',
      cssVariable: '--font-opendyslexic',
      weights: [400, 700],
      styles: ['normal'],
      fallbacks: ['sans-serif'],
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
        // Search moves out of the header and into the sidebar: the pair has to
        // change together, because Header always fills its search slot while
        // Pagefind is enabled.
        Search: './src/components/Search.astro',
        Sidebar: './src/components/Sidebar.astro',
        SocialIcons: './src/components/SocialIcons.astro',
        // Renders the breadcrumb trail above the page body.
        MarkdownContent: './src/components/MarkdownContent.astro',
        // Shipped by starlight-page-actions; it owns the Copy / Open / Share row.
        PageTitle: 'starlight-page-actions/overrides/PageTitle.astro',
        // Adds the typeface picker beside Starlight's theme picker, and the
        // pre-paint script that restores the reader's choice.
        ThemeSelect: './src/components/ThemeSelect.astro',
        Head: './src/components/Head.astro',
      },
      // Typefaces are declared in the top-level `fonts` option, not here.
      customCss: ['./src/styles/surge.css'],
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
          label: '01 · OVERVIEW',
          items: [
            { label: 'What is Kurrent Surge', slug: 'what-is-surge' },
            { label: 'Getting started', slug: 'getting-started' },
          ],
        },
        {
          label: '02 · CONNECTING',
          collapsed: true,
          items: [
            { label: 'Connection strings', slug: 'client/connection-strings' },
            { label: 'Configuring the client', slug: 'client/configuring-the-client' },
            { label: 'Clusters and load balancing', slug: 'client/clusters-and-discovery' },
            { label: 'Security', slug: 'client/security' },
            { label: 'Troubleshooting', slug: 'client/troubleshooting' },
          ],
        },
        {
          label: '03 · CLIENT',
          collapsed: true,
          items: [
            { label: 'Overview', slug: 'client' },
            {
              label: 'Streams',
              collapsed: true,
              items: [
                { label: 'Overview', slug: 'streams' },
                { label: 'Append', slug: 'streams/append' },
                { label: 'Read', slug: 'streams/read' },
                { label: 'Manage', slug: 'streams/manage' },
                { label: 'Filters', slug: 'streams/filters' },
                { label: 'Record metadata', slug: 'streams/metadata' },
              ],
            },
            { label: 'Registry', slug: 'client/registry' },
            { label: 'Indexes', slug: 'client/indexes' },
            {
              label: 'Connectors',
              collapsed: true,
              items: [
                { label: 'Managing connectors', slug: 'client/connectors' },
                { label: 'Understanding connectors', slug: 'client/connectors/intro' },
                { label: 'Features', slug: 'client/connectors/features' },
                { label: 'Configure connector', slug: 'client/connectors/settings' },
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
            { label: 'Admin', slug: 'client/admin' },
          ],
        },
        {
          label: '04 · SCHEMA',
          collapsed: true,
          items: [
            { label: 'Overview', slug: 'schema' },
            { label: 'Catalogs', slug: 'schema/catalogs' },
            { label: 'Schema names', slug: 'schema/names' },
            { label: 'Formats', slug: 'schema/formats' },
            { label: 'Registration', slug: 'schema/registration' },
            { label: 'Evolution', slug: 'schema/evolution' },
          ],
        },
        {
          label: '05 · PROCESSORS',
          collapsed: true,
          items: [
            { label: 'Overview', slug: 'processors' },
            { label: 'Writing a processor', slug: 'processors/writing-a-processor' },
            { label: 'Checkpointing', slug: 'processors/checkpointing' },
            { label: 'Partitioning', slug: 'processors/partitioning' },
            { label: 'Batching', slug: 'processors/batching' },
            { label: 'Error handling', slug: 'processors/error-handling' },
            { label: 'Observability', slug: 'processors/observability' },
          ],
        },
        {
          label: '06 · MIGRATION',
          collapsed: true,
          items: [
            { label: 'Overview', slug: 'migration' },
            { label: 'From EventStoreDB', slug: 'migration/from-eventstoredb' },
            { label: 'From KurrentDB v1', slug: 'migration/from-kurrentdb-v1' },
            { label: 'Breaking changes', slug: 'migration/breaking-changes' },
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
      ],
    }),
  ],
});
