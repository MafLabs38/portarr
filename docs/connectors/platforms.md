# Platform sources (K3s releases and support dates)

Two public services, read with GET only, tell Portarr what is newer and when support stops (D-119, [ADR 0018](../adr/0018-platforms-are-reported-from-the-tools-portarr-reads-and-two-public-sources.md)). They need no catalog entry and no credential; they are read only when a machine makes them worth reading.

| Service | Capability | Read |
|---|---|---|
| `https://update.k3s.io/v1-release/channels` | `IReleaseLineSource` | The newest K3s release of each Kubernetes line (`v1.31` is the channel of line 1.31). The `stable`, `latest` and `testing` channels and the pre-release channels are not lines and are skipped |
| `https://endoflife.date/api/v1/products/<product>/` | `ISupportLifecycleSource` | The releases of a product with the day active support ends (`eoasFrom`) and the day security fixes stop (`eolFrom`). The product is `/etc/os-release`'s `ID` of a machine (`ubuntu`, `debian`), or `kubernetes` for the lines of a cluster; one the site does not list (404, e.g. Unraid) is unknown |

## Rules

- One Kubernetes line at a time: a node on 1.31 is offered the newest 1.31 and the first version of 1.32, never 1.33.
- The end of life reached is a **danger**; within 90 days a **warning**; a newer version is **information**; nothing otherwise. A date nobody announced stays empty and is shown as unknown.
- Read at most every 6 hours (1 hour after a failure, keeping the last answer). Only what the machines make worth reading is asked: the K3s lines when a node runs K3s, a product when a machine runs it.
- The product identifier comes from a machine: it is refused unless it is a plain identifier (lower case letters, digits, dots and dashes), so it can never reshape the address.
- Every connection goes through the outbound address policy (no loopback, link-local or multicast address) and a redirect is not followed. An answer is read through a byte cap (2 MB once decompressed): a service that sends more is refused.
- The requests name the operating systems and the Kubernetes distribution the machines run (`/products/ubuntu/`, the K3s channels), nothing else.
- `PORTARR__PLATFORMUPDATES=false` turns both lookups off (an air-gapped installation, a blocked outbound traffic): the versions are still shown, with no newer version and no support date.

## Contract

Each capability has a contract suite in `Portarr.Test.Common` (`ReleaseLineSourceContract`, `SupportLifecycleSourceContract`) that every connector passes; the rules above are tested in `PlatformReportFixture` and `PlatformMonitorFixture`.
