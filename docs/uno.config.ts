import { defineConfig, presetIcons } from 'unocss';
import { iconClassNames } from './src/expressive-code/block-header.mjs';

// UnoCSS is here for one reason: the code-block header plugin names its icon as
// a class, which needs an engine to resolve into a glyph. No utility classes
// are authored by hand anywhere in this project.
export default defineConfig({
  presets: [
    presetIcons({
      autoInstall: false,
      // Without these the glyph paints but the span collapses to zero width,
      // because an icon rule alone sets no display or intrinsic size.
      extraProperties: {
        display: 'inline-block',
        width: '1.2em',
        height: '1.2em',
        'vertical-align': 'middle',
      },
    }),
  ],
  // The class is chosen at build time from a block's language, so it never
  // appears in source for UnoCSS to find. The list comes from the plugin
  // itself, so the two cannot drift apart.
  safelist: iconClassNames,
});
