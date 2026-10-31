# The Portarr reader

The reader (`portarr-reader`, [ADR 0010](../adr/0010-optional-read-only-agent.md), [ADR 0011](../adr/0011-reader-speaking-the-existing-protocols.md), D-100 to D-102) is the **simple way to connect a Docker machine**: one container on the machine, one address and one token in the catalog. It replaces the Docker socket proxy and node_exporter, and the existing connectors read it unchanged.

It is **optional**. The direct connectors ([Docker](docker.md) with a socket proxy, [node_exporter](node-exporter.md)) stay for anyone who already runs them.

## What it gives Portarr

| What Portarr reads | Where | Same as |
|---|---|---|
| The machine: CPU per core, memory, swap, load, CPU temperature | `GET /v1/host`, the node_exporter text format limited to the families Portarr uses | [node_exporter](node-exporter.md): same sampler, same formulas, same one-second cadence |
| The containers: list, usage, logs, host capacity | `GET /v1/docker/...`, a filtered subset of the Docker Engine API | The [Docker connector](docker.md) |
| What it is | `GET /v1/info` (version, host name, capabilities) | |
| Whether it runs | `GET /healthz`, the only route without the token | |

The reader returns **raw counters**: Portarr turns them into loads (D-30, D-72), so the figures are the ones node_exporter would give.

## What it costs

Measured on a 4-core Docker host with 21 containers, next to node_exporter and a socket proxy:

| | The reader | node_exporter | Socket proxy |
|---|---|---|---|
| One read of the host | about 0.6 ms, 2.4 kB | about 39 ms, 116 kB (16 kB compressed) | |
| One container list | about 3 ms | | about 3 ms |
| Memory | **under 1 MB** | about 15 MB | about 10 MB |
| Program | one 0.7 MB static binary | | |

It reads far less than node_exporter (only what Portarr uses), so it answers much faster, and one reader stands for the two tools it replaces. It is written in Rust for that reason: a probe that runs on every machine must be light ([ADR 0011](../adr/0011-reader-speaking-the-existing-protocols.md)).

## Install

```yaml
services:
  portarr-reader:
    image: ghcr.io/<owner>/portarr-reader:<tag>   # pin a version
    restart: unless-stopped
    environment:
      PORTARR_READER_TOKEN: ${PORTARR_READER_TOKEN}   # 32 characters or more: openssl rand -hex 32
    group_add:
      - "${DOCKER_GID}"   # the group that owns /var/run/docker.sock: stat -c %g /var/run/docker.sock
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
    ports:
      - "8485:8485"   # reachable from Portarr only (firewall, or a private network)
```

The image holds the reader and nothing else (no shell, no package manager, no libc), and runs as a non-root user. Its health check is the binary itself (`/portarr-reader --healthcheck`), so `docker ps` shows it as healthy. It reads the machine's counters from the container's own `/proc`, which the kernel reports for the whole host, and the CPU temperature from `/sys/class/hwmon` when the machine has a sensor.

| Variable | Default | |
|---|---|---|
| `PORTARR_READER_TOKEN` | required | The bearer token Portarr must present. At least 32 characters; the reader does not start without it. |
| `PORTARR_READER_DOCKER` | `unix:///var/run/docker.sock` | The Docker Engine API to read: a socket, or the plain `http://` URL of a read-only [socket proxy](docker.md) if you prefer not to mount the socket (HTTPS is not supported). |
| `PORTARR_READER_URLS` | `http://0.0.0.0:8485` | Where it listens (`http://+:8485` is read as every interface). |
| `PORTARR_READER_HOSTNAME` | the machine's host name | The name reported by `/v1/info`. |
| `PORTARR_READER_PROCPATH` | `/proc` | Where the host's `/proc` is, if you mount it elsewhere (for example `/host/proc` to read the host's counters from inside a container that has its own). |
| `PORTARR_READER_SYSPATH` | `/sys` | The same for `/sys` (the CPU temperature only). |

## Catalog

```yaml
environments:
  - id: server-1
    name: Server 1
    reader: { url: "http://10.0.0.10:8485", tokenEnv: PORTARR_READER_SERVER_1 }
```

- `url`: the reader's address, without credentials, query or fragment. It may carry a path when the reader sits behind a reverse proxy (`https://proxy.homelab.example/reader-1`).
- `tokenEnv`: the **name** of the environment variable that holds the token, set where Portarr runs, and it **must start with `PORTARR_READER_`** (for example `PORTARR_READER_SERVER_1`). The token never goes in the catalog, and the catalog cannot ask Portarr to send any other variable of its environment to a URL.
- `reader` excludes `docker` and `hostMetrics` on the same environment (a validation error names both): it stands for both.

## Security

- **One token, compared by hash in constant time**, never logged and never returned. Every route but `/healthz` needs it, as `Authorization: Bearer <token>` or `X-Api-Key`.
- **Read-only by construction.** Only `GET` is routed. The Docker paths and their parameters are an allowlist (host capacity, the container list, usage, logs, and an inspect reduced to the container id and its TTY flag), and every other path has no route. Container environment variables are never forwarded.
- **It needs the Docker socket, which is root-equivalent on the host.** A read-only mount does not make a socket read-only: the protection is the reader's own code (no write path, a short list of paths, a non-root user, a token). Whoever compromises the reader can act on the host. If you do not accept that, point `PORTARR_READER_DOCKER` at a [read-only socket proxy](docker.md), or keep the direct connectors.
- **Use a private network or a firewall**: the reader speaks plain HTTP, has no TLS and no request timeout of its own. Put it behind a reverse proxy for TLS.

## Verification

```bash
curl -s http://10.0.0.10:8485/healthz                              # ok
curl -s -H "Authorization: Bearer $PORTARR_READER_TOKEN" http://10.0.0.10:8485/v1/info
curl -s -H "Authorization: Bearer $PORTARR_READER_TOKEN" http://10.0.0.10:8485/v1/host | head
```

In Portarr, the machine's card on **Infrastructure** shows live metrics, and its containers appear in the app tiles. If the machine shows "the token environment variable is not set" (on its containers or its metrics alike), the variable named by `tokenEnv` is empty where Portarr runs; "http-401" means the token does not match the reader's.
