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
      POST: 0         # no writes
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
    ports:
      - "2375:2375"   # reachable from Portarr only (firewall, or a private network)
```

Then declare the environment in the catalog:

```yaml
environments:
  - { id: server-1, name: Server 1, docker: { url: "http://server-1:2375" } }
```

## Local Docker

When Portarr runs on the Docker host itself, mount the socket read-only and use `unix:///var/run/docker.sock`. Prefer the proxy in production: the socket gives full control of the host.

## What it provides

| Data | Endpoint used | Refresh |
|---|---|---|
| Containers, state, healthcheck, Compose project | `GET /containers/json?all=true` | every 15 s |
| CPU (cores) and memory (without page cache) | `GET /containers/{name}/stats?stream=false` | on demand, every 5 s on the container page |
| Logs (stdout/stderr, timestamps) | `GET /containers/{name}/logs?follow=true` | live (server-sent events) |
