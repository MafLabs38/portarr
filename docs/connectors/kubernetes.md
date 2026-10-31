# Kubernetes connector

Lists the **pods** of every namespace as workloads and the **nodes** as machines, with their live usage from metrics.k8s.io (D-55). **Read-only**: Portarr only sends GET requests to the API server (D-31), over plain HTTPS without a Kubernetes SDK.

## Read-only ServiceAccount

Portarr needs `get` and `list` on pods and nodes, and on the metrics of both. Two optional rules add data (V3 Data and backups): `list` on `apps/statefulsets` gives the size requested by StatefulSet claims, and, for the Data page's backup sources (D-81), `get, list` on `batch/cronjobs` and `batch/jobs` (a `cronjob:` source: last success, last failed attempt) and on `schedules`, `backups` and `snapshots` of the `k8up.io` group (a `k8up:` source). Without them, sizes are unknown and the copies those sources should prove are Unknown with the reason `http-403`. One more optional rule reads a node's node_exporter through the API server: `get` on `pods/proxy` in the exporter's namespace only (a `Role`, see [companions](../companions.md#in-kubernetes)); never the cluster-wide `nodes/proxy`. Nothing else: no secrets, no PersistentVolumeClaims, no writes, no `exec`.

```yaml
apiVersion: v1
kind: ServiceAccount
metadata: { name: portarr-reader, namespace: portarr }
---
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRole
metadata: { name: portarr-reader }
rules:
  - apiGroups: [""]
    resources: [pods, nodes]
    verbs: [get, list]
  - apiGroups: [metrics.k8s.io]
    resources: [pods, nodes]
    verbs: [get, list]
  - apiGroups: [apps]                 # optional: claim sizes of StatefulSets
    resources: [statefulsets]
    verbs: [list]
  - apiGroups: [batch]                # optional: backup jobs run as CronJobs (Data page)
    resources: [cronjobs, jobs]
    verbs: [get, list]
  - apiGroups: [k8up.io]              # optional: K8up schedules, backups and snapshots (Data page)
    resources: [schedules, backups, snapshots]
    verbs: [get, list]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRoleBinding
metadata: { name: portarr-reader }
roleRef: { apiGroup: rbac.authorization.k8s.io, kind: ClusterRole, name: portarr-reader }
subjects: [{ kind: ServiceAccount, name: portarr-reader, namespace: portarr }]
---
apiVersion: v1
kind: Secret
metadata:
  name: portarr-reader-token
  namespace: portarr
  annotations: { kubernetes.io/service-account.name: portarr-reader }
type: kubernetes.io/service-account-token
```

Read the token (`kubectl -n portarr get secret portarr-reader-token -o jsonpath='{.data.token}' | base64 -d`) and give it to Portarr through an environment variable (default name `K8S_TOKEN`). The token is **never written in the catalog**.

Usage needs [metrics-server](https://github.com/kubernetes-sigs/metrics-server) (shipped with k3s). Without it, nodes and pods are listed without usage.

## Catalog

```yaml
environments:
  - id: cluster
    name: Kubernetes cluster
    runtime: kubernetes
    kubernetes:
      server: "https://10.0.0.20:6443"   # the API server
      tokenEnv: K8S_TOKEN                # the variable name, not the token
      caFile: cluster-ca.crt             # cluster CA (PEM), relative to catalog.yaml

apps:
  - id: shop
    name: Shop
    workloads:
      - { runtime: kubernetes@cluster, match: { namespace: shop, app: shop-web } }
```

`server` must be an `https://` URL, since the token travels in every request (D-103). A lab or a test without TLS can add `allowInsecure: true` to accept `http://`; never do that for a real cluster.

The CA is the `certificate-authority-data` of your kubeconfig, base64-decoded (`… | base64 -d > cluster-ca.crt`): it is a public certificate, never the client key. It is the only root trusted for this server, and the host name is checked against it. Without `caFile`, the system trust store is used.

Until the token variable is set and the CA file is readable, the environment is shown as **not connected** with the reason (`token-missing`, `ca-missing`, `ca-invalid`); the rest of the catalog keeps working (D-32).

Pod names change on every rollout: match pods by `namespace` and `app` label (or `namePrefix`), see [the catalog](../catalog.md#kubernetes-selectors) (D-56).

## What it provides

| Data | Endpoint | Notes |
|---|---|---|
| Pods (workloads) | `GET /api/v1/pods` (pages of 500) | Id `namespace/pod`; state from the phase and container statuses (CrashLoopBackOff, image pull errors → restarting); readiness as health; restarts in the status text; namespace and node exposed. Every 15 s. |
| Nodes (machines) | `GET /api/v1/nodes` | Capacity (cores, memory), roles, readiness. Every 5 s. |
| Node and pod usage | `GET /apis/metrics.k8s.io/v1beta1/nodes`, `…/pods` | The node's own usage ("Node" on the card) and the sum of each pod's containers. Read every 5 s; metrics-server averages over a window (~15–20 s): a chart point is added only when a node's `timestamp` changes, and the window is shown as the resolution. |
| Usage of one pod | `GET /apis/metrics.k8s.io/v1beta1/namespaces/{ns}/pods/{name}` | Workload page, catalog pods only. |

Pod logs are not served yet.
