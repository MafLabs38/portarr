# Deploying Portarr

Portarr is one container (`ghcr.io/<owner>/portarr`) and a data folder. This page deploys it on Kubernetes with the reference manifest [`deploy/kubernetes/portarr.yaml`](../deploy/kubernetes/portarr.yaml), and shows how to let a cluster follow the releases ([releasing.md](releasing.md)) on its own.

## What the manifest creates

| Object | What it is for |
|---|---|
| `Namespace`, `ServiceAccount`, `Secret portarr-token`, `ClusterRole`, `ClusterRoleBinding` | read-only access to the cluster: nodes, namespaces, pods, workloads, metrics-server, CronJobs and K8up. No write verb, no Secret, no logs |
| `ConfigMap portarr-catalog` | the [catalog](catalog.md) as it is first seeded: what Portarr knows of your fleet. An init container copies it into the data volume (`/config/catalog`) once, and never overwrites it: the live catalog is the file on the volume, which Portarr writes (`PORTARR__CATALOG__WRITABLE=true`, D-124) |
| `Secret portarr-admin` | the password of the first administrator, created once while no user exists |
| `PersistentVolumeClaim portarr-data` | the data folder (`/config`): the SQLite database, the keys that protect the sessions, uploads, and the live catalog with its backups, audit trail and waiting changes (`/config/catalog`) |
| `Deployment` | one replica, `Recreate` (a SQLite file on a `ReadWriteOnce` volume: never two pods), non-root, read-only root file system, no capability, resource limits |
| `Service`, `Ingress` | port 8484, and a host name for Traefik (any ingress controller works) |

Your own icons go in `/config/catalog/icons` on the data volume (a ConfigMap cannot hold a folder).

Change the three values marked `CHANGE` (the administrator's password, the host name, and the version of the image), then `kubectl apply -f deploy/kubernetes/portarr.yaml`.

## Settings you will want to know

- **`PORTARR__AUTH__COOKIESECURITY=Always`**: the session cookie is always `Secure`. Behind a proxy that ends TLS, Portarr only sees plain HTTP; this keeps the cookie from ever travelling in clear.
- **The cluster itself** is declared in the catalog with `tokenEnv: PORTARR_K8S_TOKEN` (the variable the manifest fills from `portarr-token`) and `caFile: /var/run/secrets/kubernetes.io/serviceaccount/ca.crt` (mounted by Kubernetes). The catalog never holds a token ([catalog.md](catalog.md#secret-variables)).
- **Reaching the other hosts** (Docker sockets, node_exporter, Backrest) is a matter of network: the pod must reach them, and a Docker host is read through a read-only [socket proxy](connectors/docker.md), never the socket itself.
- **Credentials** for the connectors (`PORTARR_BACKREST_…`, `PORTARR_CLOUDFLARE_…`, `PORTARR_GITHUB_…`, `PORTARR_PROXMOX_…`) go in a `Secret` read as environment variables, never in the catalog.
- **Mail to the operator** (off by default, [notifications](verticals/notifications.md)): Portarr writes mail when something needs attention (one mail per category: apps, infrastructure, data, maintenance), through an SMTP server with STARTTLS (port 587; the implicit TLS of port 465 is not supported).
  The connection is set where Portarr is deployed, never from the page:

  | Variable | Meaning |
  |---|---|
  | `PORTARR__NOTIFICATIONS__SMTP__HOST` | The mail server (`smtp.homelab.example`). Needed to turn notifications on. |
  | `PORTARR__NOTIFICATIONS__SMTP__PORT` | `587` by default. |
  | `PORTARR__NOTIFICATIONS__SMTP__USERNAME` | The account that sends. Goes with the password file. |
  | `PORTARR__NOTIFICATIONS__SMTP__PASSWORDFILE` | **A file** that holds the password (a `Secret` mounted as a file, for example `/run/secrets/smtp-password`), read at each send so that it can be rotated. Never an environment variable; the API never returns it, only whether a file is set. |
  | `PORTARR__NOTIFICATIONS__SMTP__STARTTLS` | `true` by default. May be `false` only for a server without a password, such as a mail catcher on a test machine. |
  | `PORTARR__NOTIFICATIONS__ENABLED`, `…__RECIPIENT`, `…__FROM` | The starting values of what the page changes: whether Portarr writes, to whom, from which address. What is saved from Settings, Notifications wins over them. |
  | `PORTARR__NOTIFICATIONS__PASSSECONDS` | One look at what needs attention every 60 seconds by default (10 to 3600). Deployment only. |
  | `PORTARR__NOTIFICATIONS__POLICY__CONFIRMMINUTES` | A category must have had a qualifying finding for this long before its first mail: `5` by default (0 to 1440). A blip is not a mail. Deployment only. |
  | `PORTARR__NOTIFICATIONS__POLICY__CLEARMINUTES` | A category that was mailed must have been clear for this long before its episode ends: `15` by default (1 to 1440). A tool that is slow to answer after a restart does not start a new episode. Deployment only. |
  | `PORTARR__NOTIFICATIONS__POLICY__RETRYMINUTES` | A mail that could not be sent is tried again after this long: `15` by default (1 to 1440). Deployment only. |
  | `PORTARR__NOTIFICATIONS__POLICY__MAXPERDAY` | The most mails tried in 24 hours, all categories together: `10` by default (1 to 1000). The starting value of the overall cap the page changes. |

  What each category does (enabled, minimum level, reminder every N hours, maximum reminders, maximum mails a day) is not an environment variable: the operator sets it from Settings, Notifications, and the defaults are declared by Portarr (apps, infrastructure and data on, maintenance off). The policy keys are the shared package's `Notifications:Policy` section, validated at startup: a value out of range stops Portarr from starting, with the setting named.

  For a Google account: `smtp.gmail.com`, port 587, an application password in the file.

## Reaching the machines it watches

Inside the cluster, Portarr's outgoing requests carry the address of **its node** (SNAT), not a pod address. A machine that only accepts requests
from an allow-list must list that node, and Portarr should stay pinned to it (`nodeSelector`), or the firewall will drop its requests and the
machine will show as unreachable. See [Troubleshooting](troubleshooting.md#a-machine-is-unreachable).

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
