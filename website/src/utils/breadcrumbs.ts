/**
 * Derives a breadcrumb trail for the current page.
 *
 * The trail is read from `Astro.locals.starlightRoute.sidebar`, the resolved
 * tree where exactly one link carries `isCurrent`. Reading it there rather than
 * from the sidebar config means the trail follows a reorder on its own.
 */

export interface BreadcrumbItem {
  label: string;
  href?: string;
}

interface SidebarLink {
  type: 'link';
  label: string;
  href: string;
  isCurrent: boolean;
}

interface SidebarGroup {
  type: 'group';
  label: string;
  entries: SidebarEntry[];
}

type SidebarEntry = SidebarLink | SidebarGroup;

export function getBreadcrumbs(sidebar: SidebarEntry[] | undefined): BreadcrumbItem[] | undefined {
  if (!sidebar?.length) return undefined;

  const path = collapseOverview(findCurrentPath(sidebar));
  // A single crumb is the page's own title, which the H1 already states.
  if (!path || path.length < 2) return undefined;

  return path.map((entry, index) => ({
    label: entry.label,
    href: index === path.length - 1 ? undefined : resolveHref(entry),
  }));
}

function findCurrentPath(entries: SidebarEntry[]): SidebarEntry[] | undefined {
  for (const entry of entries) {
    if (entry.type === 'link') {
      if (entry.isCurrent) return [entry];
      continue;
    }

    const nested = findCurrentPath(entry.entries);
    if (nested) return [entry, ...nested];
  }

  return undefined;
}

/**
 * A group whose current page is its own "Overview" would render the group label
 * followed by "Overview", which reads as two crumbs for one destination.
 */
function collapseOverview(path: SidebarEntry[] | undefined): SidebarEntry[] | undefined {
  if (!path || path.length < 2) return path;

  const current = path[path.length - 1];
  return current && isOverview(current) ? path.slice(0, -1) : path;
}

function isOverview(entry: SidebarEntry): boolean {
  return entry.type === 'link' && entry.label.toLowerCase() === 'overview';
}

/**
 * Groups carry no href of their own. Starlight's convention is an "Overview"
 * child, so an ancestor group links there when one exists and is inert when it
 * does not.
 */
function resolveHref(entry: SidebarEntry): string | undefined {
  if (entry.type === 'link') return entry.href;

  const overview = entry.entries.find((child) => isOverview(child));
  return overview && overview.type === 'link' ? overview.href : undefined;
}
