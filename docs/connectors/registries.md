# Container registries connector

Looks up the tags of the images the fleet runs, for the **Deployed images** tab of the Maintenance page (D-66). **Anonymous and read-only**: GET and HEAD requests only, no credentials, nothing is pulled.

## Registries

| Registry | How |
|---|---|
| Docker Hub (`docker.io`, bare names) | `hub.docker.com/v2/namespaces/{ns}/repositories/{name}/tags?ordering=last_updated`: tags newest first with their date and digest, outside the pull rate limit; 3 pages of 100 |
| Any OCI registry (GHCR, lscr.io, Quay, gcr.io, registry.k8s.io…) | `GET /v2/{repository}/tags/list`, after the anonymous token flow of the registry's `WWW-Authenticate: Bearer realm=…` challenge (HTTPS realms only, pull scope of that repository only); 10 pages of 1,000 tags. The newest tag's date comes from its image configuration (manifest, then config blob) |

Private images are reported as "registry refused anonymous access"; registry credentials come later.

## Cadence

Each image repository is looked up every **6 hours** (1 hour after a failure), new images within a minute, at most 25 lookups per minute. Results are kept in memory only.

## Verdicts

| Verdict | When |
|---|---|
| Up to date | no newer tag of the same shape (`16-alpine` is only compared with `N-alpine`) |
| Major / Minor / Patch / New build | a newer tag of the same shape; the first number that differs, or only the build suffix (`-ls12` → `-ls13`) |
| Rebuilt | a floating tag (`latest`) whose registry digest is not the running one (Kubernetes reports the running digest) |
| Floating | a floating tag without running digest nor a usable version label: cannot tell |
| Unknown | started from an image id (`sha256:…`), not in the registry, private, rate limited or unreachable: the reason is shown |

**Docker containers shown with an image id** (`docker ps` shows `sha256:…` as the image) cannot be checked: Docker reports the id when the container was created from one, or when the tag it was created from now points to another image (a newer image was pulled, the container was not recreated). Recreate them from a tagged reference (`image: lscr.io/linuxserver/sonarr:4`) to have them checked; their OCI version label is still shown as the current version.
