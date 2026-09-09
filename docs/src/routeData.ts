import { defineRouteMiddleware } from '@astrojs/starlight/route-data';

// Starlight emits og:title, og:description, og:url and twitter:card, but has no
// option for the image, and `summary_large_image` renders an empty card without
// one. The `head` config option is global, so it cannot carry a per-page image;
// route middleware can, because it runs per render and owns that page's head.
//
// Cards come from `src/pages/og/[...route].ts`, keyed by the same route id.
export const onRequest = defineRouteMiddleware((context) => {
  const { id, head } = context.locals.starlightRoute;

  // Social crawlers do not resolve relative paths.
  const image = new URL(`${import.meta.env.BASE_URL}og/${id || 'index'}.png`, context.site).href;

  head.push(
    { tag: 'meta', attrs: { property: 'og:image', content: image } },
    { tag: 'meta', attrs: { property: 'og:image:width', content: '1200' } },
    { tag: 'meta', attrs: { property: 'og:image:height', content: '630' } },
    { tag: 'meta', attrs: { name: 'twitter:image', content: image } }
  );
});
