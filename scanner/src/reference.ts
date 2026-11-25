// What the scanner accepts as an image: a plain reference, nothing the CLI could take for an option or a shell could
// interpret. The reference is also passed as one argument of an argument array, never through a shell.

export const maxImages = 200;
export const maxReferenceLength = 300;

/** `registry.example:5000/team/app:1.2@sha256:…`: names, a tag, a digest. Starts with a letter or a digit. */
const referencePattern = /^[A-Za-z0-9][A-Za-z0-9._/:@-]*$/;

export function isImageReference(value: unknown): value is string {
  return typeof value === 'string' && value.length <= maxReferenceLength && referencePattern.test(value);
}

/** `owner/name` of a GitHub repository: the two parts of the API path, never an option, a traversal or a query. */
const releasePattern = /^[A-Za-z0-9._-]{1,100}\/[A-Za-z0-9._-]{1,100}$/;

export function isReleaseReference(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    releasePattern.test(value) &&
    !value.startsWith('-') &&
    !value.startsWith('.') &&
    // `owner/..` would walk out of the repository's path once the URL is normalised.
    !value.split('/').some((part) => part === '.' || part === '..')
  );
}

/** What can be scanned: a container image, or the SBOM published with the latest release of a GitHub repository. */
export type TargetKind = 'image' | 'release';
export const targetKinds: readonly TargetKind[] = ['image', 'release'];

export interface Target {
  kind: TargetKind;
  ref: string;
}

/** One key per target: the pair, so that an image and a release with the same text are two things. */
export function targetKey(kind: TargetKind, ref: string): string {
  return `${kind}:${ref}`;
}

/** The first problem of a request body's `targets`, or the validated targets (deduplicated by kind and ref, in order). */
export function parseTargets(value: unknown): { targets: Target[] } | { error: string } {
  if (!Array.isArray(value) || value.length === 0) {
    return {
      error:
        'targets must be a non-empty list of { "kind": "image", "ref": "registry/repository:tag" } or { "kind": "release", "ref": "owner/name" }.',
    };
  }
  if (value.length > maxImages) {
    return { error: `targets holds at most ${maxImages} entries.` };
  }
  const seen = new Set<string>();
  const targets: Target[] = [];
  for (const entry of value as ({ kind?: unknown; ref?: unknown } | null)[]) {
    const target = targetOf(entry);
    if ('error' in target) {
      return target;
    }
    const key = targetKey(target.kind, target.ref);
    if (!seen.has(key)) {
      seen.add(key);
      targets.push(target);
    }
  }
  return { targets };
}

function targetOf(entry: { kind?: unknown; ref?: unknown } | null): Target | { error: string } {
  const kind = typeof entry === 'object' && entry !== null ? targetKinds.find((k) => k === entry.kind) : undefined;
  if (kind === undefined || entry === null) {
    return { error: `a target's kind must be one of: ${targetKinds.join(', ')}.` };
  }
  if (kind === 'image' && !isImageReference(entry.ref)) {
    return {
      error: `an image target's ref must be a plain image reference of at most ${maxReferenceLength} characters (registry/repository:tag).`,
    };
  }
  if (kind === 'release' && !isReleaseReference(entry.ref)) {
    return { error: "a release target's ref must be a GitHub repository, owner/name." };
  }
  return { kind, ref: entry.ref as string };
}
