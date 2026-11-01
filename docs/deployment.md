# Deploying Portarr

Portarr is one container (`ghcr.io/<owner>/portarr`) and a data folder. This page deploys it on Kubernetes with the reference manifest [`deploy/kubernetes/portarr.yaml`](../deploy/kubernetes/portarr.yaml), and shows how to let a cluster follow the releases ([releasing.md](releasing.md)) on its own.

## What the manifest creates

| Object | What it is for |
|---|---|
| `Namespace`, `ServiceAccount`, `Secret portarr-token`, `ClusterRole`, `ClusterRoleBinding` | read-only access to the cluster: nodes, namespaces, pods, workloads, metrics-server, CronJobs and K8up. No write verb, no Secret, no logs |
| `ConfigMap portarr-catalog` | the [catalog](catalog.md): what Portarr knows of your fleet. Read-only in the pod (`PORTARR__CATALOG__WRITABLE=false`): the ConfigMap is the source of truth |
| `Secret portarr-admin` | the password of the first administrator, created once while no user exists |
| `PersistentVolumeClaim portarr-data` | the data folder (`/config`): the SQLite database, the keys that protect the sessions, uploads |
| `Deployment` | one replica, `Recreate` (a SQLite file on a `ReadWriteOnce` volume: never two pods), non-root, read-only root file system, no capability, resource limits |
| `Service`, `Ingress` | port 8484, and a host name for Traefik (any ingress controller works) |

Change the three values marked `CHANGE` (the administrator's password, the host name, and the version of the image), then `kubectl apply -f deploy/kubernetes/portarr.yaml`.

## Settings you will want to know

- **`PORTARR__AUTH__COOKIESECURITY=Always`**: the session cookie is always `Secure`. Behind a proxy that ends TLS, Portarr only sees plain HTTP; this keeps the cookie from ever travelling in clear.
- **The cluster itself** is declared in the catalog with `tokenEnv: PORTARR_K8S_TOKEN` (the variable the manifest fills from `portarr-token`) and `caFile: /var/run/secrets/kubernetes.io/serviceaccount/ca.crt` (mounted by Kubernetes). The catalog never holds a token ([catalog.md](catalog.md#secret-variables)).
- **Reaching the other hosts** (Docker sockets, node_exporter, Backrest) is a matter of network: the pod must reach them, and a Docker host is read through a read-only [socket proxy](connectors/docker.md), never the socket itself.
- **Credentials** for the connectors (`PORTARR_BACKREST_…`, `PORTARR_CLOUDFLARE_…`, `PORTARR_GITHUB_…`) go in a `Secret` read as environment variables, never in the catalog.

## Following the releases

Every release publishes the images `X.Y.Z` and `latest`. Two ways to deploy a version:

1. **By hand**: change the `image:` line and apply.
2. **By itself (GitOps)**: keep the manifests in a git repository and let [Flux](https://fluxcd.io) apply them. Flux's image automation watches the registry, picks the newest tag that matches a range, and commits it to the repository; the cluster then follows the commit. Nothing outside the cluster ever holds a credential for it.

```yaml
apiVersion: image.toolkit.fluxcd.io/v1
kind: ImageRepository
metadata: { name: portarr, namespace: flux-system }
spec: { image: ghcr.io/<owner>/portarr, interval: 5m }
---
apiVersion: image.toolkit.fluxcd.io/v1
kind: ImagePolicy
metadata: { name: portarr, namespace: flux-system }
spec:
  imageRepositoryRef: { name: portarr }
  policy: { semver: { range: ">=0.1.0" } }   # tags such as develop and sha-... are not versions: they are ignored
```

and, in the Deployment, a marker Flux rewrites when a newer version appears:

```yaml
image: ghcr.io/<owner>/portarr:0.1.0 # {"$imagepolicy": "flux-system:portarr"}
```

## Updating, rolling back

A new version is a new tag: the pod restarts on it, and the database is migrated at startup. To go back, put the previous tag back (with Flux: `git revert` the commit that moved it). Back up the data folder before a minor version; the migrations only go forward.
