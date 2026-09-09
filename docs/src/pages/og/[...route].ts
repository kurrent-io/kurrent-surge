import { getCollection } from 'astro:content';
import { OGImageRoute } from 'astro-og-canvas';

// One card per docs page. Keys become the route, so `streams/append` renders at
// `/og/streams/append.png`.
const entries = await getCollection('docs');
const pages = Object.fromEntries(entries.map(({ id, data }) => [id, data]));

export const { getStaticPaths, GET } = await OGImageRoute({
  pages,
  getImageOptions: (_path, page) => ({
    title: page.title,
    description: page.description,
    logo: { path: './src/assets/logo.png', size: [180] },
    // Brand tokens read off the rendered site: --sl-color-bg and --accent.
    bgGradient: [
      [15, 24, 31],
      [12, 30, 44],
    ],
    border: { color: [96, 194, 255], width: 12, side: 'block-end' },
    padding: 72,
  }),
});
