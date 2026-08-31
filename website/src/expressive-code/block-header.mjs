import { h } from 'hastscript';

// Every code block gets a header with its language glyph. A block that sets
// `title="Program.cs"` shows the filename beside the icon; one that does not
// shows the icon alone.
//
// Expressive Code only builds a header when a block has a title, so an untitled
// block is given a blank one. That is cheaper than assembling the header here,
// and it keeps a single rendering path for both cases.
//
// The icon class is resolved by UnoCSS at build time; uno.config.ts safelists
// the values below, because they never appear in source for it to scan.

const ICONS = {
  csharp: 'csharp',
  cs: 'csharp',
  css: 'css',
  diff: 'git',
  html: 'html',
  http: 'http',
  ini: 'settings',
  js: 'javascript',
  javascript: 'javascript',
  json: 'json',
  jsonc: 'json',
  md: 'markdown',
  markdown: 'markdown',
  sql: 'database',
  text: 'document',
  toml: 'settings',
  ts: 'typescript',
  typescript: 'typescript',
  xml: 'xml',
  yaml: 'yaml',
  yml: 'yaml',
};

// Expressive Code gives these a terminal frame: window dots, no header.
const TERMINAL_LANGUAGES = new Set([
  'bash', 'bat', 'batch', 'cmd', 'console', 'nu', 'nushell', 'powershell',
  'ps', 'ps1', 'psd1', 'psm1', 'sh', 'shell', 'shellscript', 'shellsession', 'zsh',
]);

export const iconClassNames = [...new Set(Object.values(ICONS))].map(
  (icon) => `i-material-icon-theme:${icon}`
);

const isDecorated = (codeBlock) =>
  !TERMINAL_LANGUAGES.has(codeBlock.language) && !!ICONS[codeBlock.language];

export function pluginBlockHeader() {
  return {
    name: 'Block header icon',
    hooks: {
      preprocessMetadata({ codeBlock }) {
        if (codeBlock.props.title) return;
        if (!isDecorated(codeBlock)) return;

        codeBlock.props.title = ' ';
      },

      postprocessRenderedBlock({ codeBlock, renderData }) {
        if (!isDecorated(codeBlock)) return;

        const ast = renderData?.blockAst;
        if (!ast || !('children' in ast)) return;

        const figcaption = ast.children.find(
          (child) => child.type === 'element' && child.tagName === 'figcaption'
        );
        const title = figcaption?.children?.find(
          (child) =>
            child.type === 'element' &&
            child.tagName === 'span' &&
            child.properties?.className?.includes('title')
        );
        if (!title) return;

        title.children ??= [];
        title.children.unshift(
          h('span', {
            className: [`i-material-icon-theme:${ICONS[codeBlock.language]}`, 'code-block-icon'],
          })
        );
      },
    },
  };
}
