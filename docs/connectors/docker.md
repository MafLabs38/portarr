# Docker connector

Reads containers, their healthcheck, their live CPU and memory usage, and their logs from the Docker Engine API. **Read-only**: Portarr only sends GET requests (D-31).

## Recommended: a read-only socket proxy

Never expose the Docker socket itself over the network. Run [docker-socket-proxy](https://github.com/Tecnativa/docker-socket-proxy) on each Docker host, allowing containers only:

```yaml
services:
  socket-proxy:
    image: tecnativa/docker-socket-proxy:v0.4.2
    restart: unless-stopped
    environment:
      CONTAINERS: 1   # list, inspect, stats, logs
      INFO: 1         # optional: host CPU count and memory for the Infrastructure page
      POST: 0         # no writes
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
    ports:
      - "2375:2375"   # reachable from Portarr only (firewall, or a private network)
```

> **What `CONTAINERS=1` gives away**: every GET under `/containers`, including `inspect` (environment variables, so secrets), logs and container file exports. Treat port 2375 as sensitive as the secrets of every container on the host: bind it to one interface and allow only Portarr's host (firewall rule, or a private Docker network shared with Portarr so nothing is published at all). Pin the proxy image by digest in production.

> **Until authentication exists (C2)**: `GET /api/v1/workloads` lists every container of every environment (names, images, state) to anyone who can reach Portarr. Do not expose Portarr directly: put it behind your reverse proxy's authentication (forward auth, OIDC, basic auth) or keep it on a private network meanwhile.

Then declare the environment in the catalog:

```yaml
environments:
  - { id: server-1, name: Server 1, docker: { url: "http://server-1:2375" } }
```

## Behind a proxy that asks for a token

`docker.url` may carry a base path (a reverse proxy serving the API under `/docker/`), and `docker.tokenEnv` names the environment variable that holds a bearer token to send with every request. The variable must start with `PORTARR_READER_`: a catalog can be edited through the API, and only that namespace is ever sent to a catalog URL ([the reader](reader.md), D-102). When the variable is empty the machine is shown as `token-missing` instead of being read anonymously. For a machine with a [reader](reader.md), declare `reader:` instead: it sets both.

## Local Docker

When Portarr runs on the Docker host itself, it can use `unix:///var/run/docker.sock` directly, but avoid it outside a disposable test setup: mounting the socket with `:ro` does not restrict the Docker API. Whoever holds the socket can start a privileged container, so a direct mount gives root-equivalent access to the host. Run the socket proxy next to Portarr instead (`docker: { url: "http://socket-proxy:2375" }` on a private Docker network).

## What it provides

| Data | Endpoint used | Refresh |
|---|---|---|
| Containers, state, healthcheck, Compose project, mounts (bind mounts and named volumes: where their data lives, V3) | `GET /containers/json?all=true` | every 15 s |
| CPU (cores) and memory (without page cache) | `GET /containers/{name}/stats?stream=false` | on demand, every 5 s on the container page |
| Logs (stdout/stderr, timestamps) | `GET /containers/{name}/logs?follow=true` | live (server-sent events) |

## Machine card (Infrastructure)

A Docker environment is one machine on the Infrastructure page (D-54). Every 3 s Portarr takes a one-shot stats sample of each running container (`GET /containers/{id}/stats?stream=false&one-shot=true`, 4 at a time: about 0.1 s for 40 containers) and measures CPU between two samples. The machine's usage is the **sum of its containers**, labelled "Containers" (D-57): Docker does not see the host's own processes, VMs or page cache. Its capacity (cores, memory) comes from `GET /info` when the proxy allows `INFO=1`, else from the containers' stats (online CPUs, and the memory limit of an unlimited container, which is the host's memory).

Host-level usage needs a host metrics source (node_exporter or Prometheus), planned as an integration; none is shipped yet.
