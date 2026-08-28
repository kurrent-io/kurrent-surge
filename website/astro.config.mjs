// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightLlmsTxt from 'starlight-llms-txt';
import starlightLinksValidator from 'starlight-links-validator';

// GitHub Pages serves from /<repo>/ while Cloudflare serves from /. Setting the base
// through an env var keeps every internal link correct on both without a code change.
const base = process.env.SITE_BASE ?? '/';

// Starlight's sitemap integration needs an absolute origin and silently skips without
// one. Same env-var treatment so a deploy target sets both together.
const site = process.env.SITE_URL ?? 'https://kurrent-io.github.io';

export default defineConfig({
  site,
  base,
  integrations: [
    starlight({
      title: 'Kurrent Surge',
      description: 'The .NET SDK for the Kurrent Platform.',
      // Dark-only, as kcap-web does: brand.css defines one palette, and its
      // Starlight mapping gives the light scope the same values purely as a
      // fallback. A visible toggle that changes nothing reads as broken.
      expressiveCode: {
        themes: ['starlight-dark'],
        useStarlightDarkModeSwitch: false,
        // starlight-dark ships a blue-tinted editor ground that fights the
        // near-black page. The theme file asks for #141414 in CSS, but
        // expressive-code sets its own background from the theme at a higher
        // specificity — these are the supported keys that actually win.
        styleOverrides: {
          borderRadius: '10px',
          borderColor: 'rgba(255, 255, 255, 0.06)',
          codeBackground: '#141414',
          frames: {
            editorBackground: '#141414',
            terminalBackground: '#141414',
            // The titlebar and tab bar carry their own colours from the theme
            // and keep starlight-dark's blue tint unless named separately.
            terminalTitlebarBackground: '#141414',
            terminalTitlebarForeground: 'var(--fg-dim)',
            terminalTitlebarBorderBottomColor: 'var(--border)',
            editorTabBarBackground: '#141414',
            editorTabBarBorderBottomColor: 'var(--border)',
            editorActiveTabBackground: '#141414',
            editorActiveTabForeground: 'var(--fg)',
            editorActiveTabIndicatorTopColor: 'var(--coral)',
            frameBoxShadowCssValue: 'none',
          },
        },
      },
      components: {
        // Hiding the toggle is not enough on its own: Starlight's default
        // ThemeProvider still reads localStorage and prefers-color-scheme, so a
        // visitor whose system is light would land on the light palette the
        // brand does not define. These two overrides lock it.
        ThemeProvider: './src/components/DocsThemeProvider.astro',
        ThemeSelect: './src/components/DocsThemeSelect.astro',
      },
      // Order matters: faces, then tokens, then the primitives that consume
      // them, then the Starlight mapping. The first four are ported from
      // kcap-web so the SDK docs and capacitor.kurrent.io read as one brand.
      // primitives.css is loaded here as well as on the landing page — that is
      // what puts the brand's buttons, eyebrows and glow on every docs route
      // rather than on the home page alone.
      customCss: [
        './src/styles/fonts.css',
        './src/styles/brand.css',
        './src/styles/primitives.css',
        './src/styles/starlight-theme.css',
        // Ours, not ported — kcap-web's docs have no landing page and no CTA.
        './src/styles/docs.css',
      ],
      social: [
        {
          icon: 'github',
          label: 'GitHub',
          href: 'https://github.com/kurrent-io/kurrent-sdk-dotnet',
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
          label: '01 · GET STARTED',
          items: [{ label: 'Getting started', slug: 'getting-started' }],
        },
        {
          label: '02 · PROCESSORS',
          collapsed: true,
          items: [
            { label: 'Overview', slug: 'processors' },
            { label: 'Writing a processor', slug: 'processors/writing-a-processor' },
            { label: 'Checkpointing', slug: 'processors/checkpointing' },
            { label: 'Partitioning', slug: 'processors/partitioning' },
            { label: 'Batching', slug: 'processors/batching' },
          ],
        },
        {
          label: '03 · SCHEMA',
          collapsed: true,
          items: [
            { label: 'Overview', slug: 'schema' },
            { label: 'Catalogs', slug: 'schema/catalogs' },
            { label: 'Registration', slug: 'schema/registration' },
            { label: 'Formats', slug: 'schema/formats' },
          ],
        },
        {
          label: '04 · CLIENT',
          collapsed: true,
          items: [
            { label: 'Overview', slug: 'client' },
            { label: 'Streams', slug: 'client/streams' },
            { label: 'Registry', slug: 'client/registry' },
            { label: 'Indexes', slug: 'client/indexes' },
            { label: 'Connectors', slug: 'client/connectors' },
            { label: 'Admin', slug: 'client/admin' },
          ],
        },
      ],
      plugins: [
        // Emits /llms.txt so a coding agent can consume these docs directly.
        starlightLlmsTxt(),
        // Fails the build on a broken internal link. Cheap insurance while the
        // page set is still moving.
        starlightLinksValidator(),
      ],
    }),
  ],
});
