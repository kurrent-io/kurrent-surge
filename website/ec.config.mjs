import { pluginBlockHeader } from './src/expressive-code/block-header.mjs';

// The whole Expressive Code config lives here, not under `expressiveCode` in
// astro.config.mjs. Setting that key makes Starlight skip this file, and
// plugins registered there never run.
//
// Expressive Code themes are full VS Code themes: they carry editor chrome
// colours as well as syntax tokens. The two Starlight themes below are kept for
// their syntax colours, and every chrome colour is named here — with
// `useStarlightUiThemeColors` off, anything left unset falls through to the
// theme, which is how a stray navy border appeared once before.
//
// The surface relationships are taken from aspire.dev, measured rather than
// guessed: the tab bar is the darkest strip, and the active tab is the SAME
// colour as the code area so the two read as one panel hanging off the bar.
/** @type {import('@astrojs/starlight/expressive-code').StarlightExpressiveCodeOptions} */
export default {
  themes: ['starlight-dark', 'starlight-light'],
  plugins: [pluginBlockHeader()],
  useStarlightUiThemeColors: false,
  styleOverrides: {
    borderRadius: '0.5rem',
    borderColor: 'var(--border-1)',
    codeBackground: 'var(--bg-1)',
    frames: {
      editorBackground: 'var(--bg-1)',
      // Same as the bar, so the header reads as one uniform strip rather than a
      // chip sitting on it. The accent underline is what marks the tab.
      editorActiveTabBackground: 'var(--bg)',
      editorActiveTabForeground: 'var(--fg)',
      editorActiveTabIndicatorBottomColor: 'var(--accent)',
      editorActiveTabIndicatorTopColor: 'transparent',
      // The bar behind the tab, one step darker so the tab reads as raised.
      editorTabBarBackground: 'var(--bg)',
      editorTabBarBorderBottomColor: 'var(--border-1)',
      inlineButtonBorder: 'var(--border-1)',
      inlineButtonForeground: 'var(--fg-dim)',
      terminalBackground: 'var(--bg-1)',
      terminalTitlebarBackground: 'var(--bg)',
      terminalTitlebarForeground: 'var(--fg-dim)',
      terminalTitlebarBorderBottomColor: 'var(--border-1)',
      // A drop shadow alone is invisible here: the page is #0a0a0a, so nothing
      // can fall darker than the ground. Dark interfaces signal elevation with a
      // light top edge instead — the inset highlight — with the soft dark
      // shadow left underneath for where the block crosses the ambient wash.
      frameBoxShadowCssValue:
        'inset 0 1px 0 rgba(245, 243, 240, 0.06), 0 2px 10px rgba(0, 0, 0, 0.55)',
    },
  },
};
