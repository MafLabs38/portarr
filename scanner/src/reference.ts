// What the scanner accepts as an image: a plain reference, nothing the CLI could take for an option or a shell could
// interpret. The reference is also passed as one argument of an argument array, never through a shell.

export const maxImages = 200;
export const maxReferenceLength = 300;

/** `registry.example:5000/team/app:1.2@sha256:…`: names, a tag, a digest. Starts with a letter or a digit. */
const referencePattern = /^[A-Za-z0-9][A-Za-z0-9._/:@-]*$/;

export function isImageReference(value: unknown): value is string {
  return typeof value === 'string' && value.length <= maxReferenceLength && referencePattern.test(value);
}

/** What can be scanned. Only container images for now; a later kind (an SBOM of a release) is one more entry here. */
export type TargetKind = 'image';
export const targetKinds: readonly string[] = ['image'];

export interface Target {
  kind: TargetKind;
  ref: string;
}

/** The first problem of a request body's `targets`, or the validated targets (deduplicated, in order). */
export function parseTargets(value: unknown): { targets: Target[] } | { error: string } {
  if (!Array.isArray(value) || value.length === 0) {
    return { error: 'targets must be a non-empty list of { "kind": "image", "ref": "registry/repository:tag" }.' };
  }
  if (value.length > maxImages) {
    return { error: `targets holds at most ${maxImages} entries.` };
  }
  const seen = new Set<string>();
  const targets: Target[] = [];
  for (const entry of value as ({ kind?: unknown; ref?: unknown } | null)[]) {
    if (
      typeof entry !== 'object' ||
      entry === null ||
      typeof entry.kind !== 'string' ||
      !targetKinds.includes(entry.kind)
    ) {
      return { error: `a target's kind must be one of: ${targetKinds.join(', ')}.` };
    }
    if (!isImageReference(entry.ref)) {
      return {
        error: `an image target's ref must be a plain image reference of at most ${maxReferenceLength} characters (registry/repository:tag).`,
      };
    }
    if (!seen.has(entry.ref)) {
      seen.add(entry.ref);
      targets.push({ kind: 'image', ref: entry.ref });
    }
  }
  return { targets };
}
