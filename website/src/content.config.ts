import { defineCollection } from 'astro:content';
import { z } from 'astro/zod';
import { docsLoader } from '@astrojs/starlight/loaders';
import { docsSchema } from '@astrojs/starlight/schema';

export const collections = {
  docs: defineCollection({
    loader: docsLoader(),
    schema: docsSchema({
      extend: z.object({
        // Renders the licence badge beside the page title. Set in frontmatter
        // rather than in the body so the badge belongs to the heading and
        // cannot drift below the breadcrumb as the page grows.
        licenseRequired: z.boolean().optional(),
      }),
    }),
  }),
};
