import type { DeployedImage, Platform, Repository } from 'typings/Maintenance';

export const codeTab = 'code';
export const imagesTab = 'images';
export const platformsTab = 'platforms';

export type MaintenanceTab = typeof codeTab | typeof imagesTab | typeof platformsTab;

/** Filters of the "My code" tab: the repositories a figure of the summary or a finding counts. */
export const repositoryFilters = {
  severe: (r: Repository) =>
    r.dependabot.critical + r.dependabot.high + r.codeScanning.critical + r.codeScanning.high > 0,
  moderate: (r: Repository) => r.dependabot.medium + r.dependabot.low + r.codeScanning.medium + r.codeScanning.low > 0,
  'other-alerts': (r: Repository) =>
    r.dependabot.total -
      r.dependabot.critical -
      r.dependabot.high +
      r.codeScanning.total -
      r.codeScanning.critical -
      r.codeScanning.high >
    0,
  secrets: (r: Repository) => r.secretScanning.total > 0,
  'ci-failing': (r: Repository) => r.ci?.failed === true,
  'dependabot-off': (r: Repository) => r.dependabot.state === 'off',
  'code-scanning-off': (r: Repository) => r.codeScanning.state === 'off',
  'dependabot-prs': (r: Repository) => (r.dependabotPullRequests ?? 0) > 0,
} as const satisfies Record<string, (repository: Repository) => boolean>;

/** Filters of the "Deployed images" tab. */
export const imageFilters = {
  'up-to-date': (i: DeployedImage) => i.status === 'up-to-date',
  behind: (i: DeployedImage) => i.status === 'behind' || i.status === 'newer-build',
  major: (i: DeployedImage) => i.status === 'behind' && i.change === 'major',
  'behind-minor': (i: DeployedImage) => i.status === 'behind' && i.change !== 'major',
  rebuilt: (i: DeployedImage) => i.status === 'newer-build',
  'cannot-tell': (i: DeployedImage) => i.status === 'floating' || i.status === 'unknown' || i.status === 'pending',
  unknown: (i: DeployedImage) => i.status === 'unknown',
} as const satisfies Record<string, (image: DeployedImage) => boolean>;

/** Filters of the "Platforms" tab: what each finding of the attention list counts. */
export const platformFilters = {
  'platform-out-of-support': (p: Platform) => p.level === 'danger',
  'platform-support-ending': (p: Platform) => p.level === 'warning',
  'platform-behind': (p: Platform) => p.level === 'info',
  'platform-lookup-failed': (p: Platform) => p.lookup === 'failed',
} as const satisfies Record<string, (platform: Platform) => boolean>;

export type RepositoryFilter = keyof typeof repositoryFilters;
export type ImageFilter = keyof typeof imageFilters;
export type PlatformFilter = keyof typeof platformFilters;
export type MaintenanceFilter = RepositoryFilter | ImageFilter | PlatformFilter;

export function isRepositoryFilter(id: string | null): id is RepositoryFilter {
  return id !== null && Object.hasOwn(repositoryFilters, id);
}

export function isImageFilter(id: string | null): id is ImageFilter {
  return id !== null && Object.hasOwn(imageFilters, id);
}

export function isPlatformFilter(id: string | null): id is PlatformFilter {
  return id !== null && Object.hasOwn(platformFilters, id);
}

/** The filter of the URL, when it exists and belongs to the tab shown: anything else is ignored. */
export function activeFilter(tab: MaintenanceTab, id: string | null): MaintenanceFilter | null {
  if (tab === codeTab) {
    return isRepositoryFilter(id) ? id : null;
  }
  if (tab === platformsTab) {
    return isPlatformFilter(id) ? id : null;
  }
  return isImageFilter(id) ? id : null;
}

export function filterRepositories(repositories: Repository[], filter: MaintenanceFilter | null): Repository[] {
  return isRepositoryFilter(filter) ? repositories.filter(repositoryFilters[filter]) : repositories;
}

export function filterImages(images: DeployedImage[], filter: MaintenanceFilter | null): DeployedImage[] {
  return isImageFilter(filter) ? images.filter(imageFilters[filter]) : images;
}

export function filterPlatforms(platforms: Platform[], filter: MaintenanceFilter | null): Platform[] {
  return isPlatformFilter(filter) ? platforms.filter(platformFilters[filter]) : platforms;
}

/** The query string that shows a tab, optionally filtered (shareable, and each filter is a history entry). */
export function maintenanceSearch(tab: MaintenanceTab, filter?: MaintenanceFilter): string {
  const params = new URLSearchParams();
  if (tab !== codeTab) {
    params.set('tab', tab);
  }
  if (filter) {
    params.set('filter', filter);
  }
  const search = params.toString();
  return search ? `?${search}` : '?';
}

/** The tab a finding of the attention list ("kind") filters, and the filter. Null: nothing to filter. */
const attentionFilters: Record<string, { tab: MaintenanceTab; filter: MaintenanceFilter } | undefined> = {
  'severe-alerts': { tab: codeTab, filter: 'severe' },
  'secret-alerts': { tab: codeTab, filter: 'secrets' },
  'ci-failing': { tab: codeTab, filter: 'ci-failing' },
  'other-alerts': { tab: codeTab, filter: 'other-alerts' },
  'dependabot-off': { tab: codeTab, filter: 'dependabot-off' },
  'code-scanning-off': { tab: codeTab, filter: 'code-scanning-off' },
  'dependabot-prs': { tab: codeTab, filter: 'dependabot-prs' },
  'image-major': { tab: imagesTab, filter: 'major' },
  'images-behind': { tab: imagesTab, filter: 'behind-minor' },
  'image-rebuilt': { tab: imagesTab, filter: 'rebuilt' },
  'images-unknown': { tab: imagesTab, filter: 'unknown' },
  'platform-out-of-support': { tab: platformsTab, filter: 'platform-out-of-support' },
  'platform-support-ending': { tab: platformsTab, filter: 'platform-support-ending' },
  'platforms-behind': { tab: platformsTab, filter: 'platform-behind' },
  'platform-lookup-failed': { tab: platformsTab, filter: 'platform-lookup-failed' },
};

export function attentionSearch(kind: string): string | null {
  const target = attentionFilters[kind];
  return target ? maintenanceSearch(target.tab, target.filter) : null;
}
