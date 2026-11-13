# GPU exporter connector

Reads the graphics card of a Docker host from an **NVIDIA GPU exporter** every 5 seconds (D-120, [ADR 0019](../adr/0019-the-gpu-is-read-from-a-standard-exporter.md)). Installation: [companions.md](../companions.md#nvidia-gpu-exporter). Catalog: `nvidiaGpu: { exporter: <URL> }` on a Docker environment ([catalog.md](../catalog.md)).

## What is read

One `GET` of the `/metrics` URL, gzip accepted, 5 s timeout, no redirects, no credentials. The answer is streamed line by line; only these families are parsed (every other line is skipped unread, an answer over one million characters is refused). Each carries the card's `uuid`:

| Metric | Use |
|---|---|
| `nvidia_smi_gpu_info{index,name,driver_version}` | The card's model and driver; cards are ordered by `index` |
| `nvidia_smi_utilization_gpu_ratio`, `_encoder_ratio`, `_decoder_ratio` | Load, video encoder and decoder, as percent (the exporter's ratio 0 to 1 times 100, kept between 0 and 100) |
| `nvidia_smi_memory_used_bytes`, `_total_bytes` | Memory |
| `nvidia_smi_encoder_stats_session_count` | Encoder sessions |
| `nvidia_smi_temperature_gpu` | Temperature, °C |
| `nvidia_smi_power_draw_watts`, `_power_limit_watts` (or `_enforced_power_limit_watts`) | Power |

A figure the exporter does not give is shown as missing. A failed read says why (`unreachable`, `timeout`, `http-503`…) and the last values stay.

## What is covered

NVIDIA cards only: the section is named after the tool (`nvidiaGpu`), so another vendor's exporter is one more connector and one more section, not a change to this one. The card is read every 5 seconds, a fixed interval (the exporter asks the driver on each read). A Docker host only.

## The containers attached to the card

The running containers created with the NVIDIA runtime (`HostConfig.Runtime`) or a GPU device request (`HostConfig.DeviceRequests`: the `nvidia` driver or the `gpu` capability) are listed under the card ("Attached to"). A container given the device by path (`/dev/dri`) is not. This is read with `docker inspect` (read-only, through the same Docker endpoint as the inventory), once per container for as long as it exists, at most 20 per pass. **It says which containers can use the card, not how much each uses it.**

## Contract

`GpuSourceContract` (in `Portarr.Test.Common`) is run against the connector: it supports the environments that declare a card and no other, gives one reading per card with an identifier and a name and loads between 0 and 100, gives nothing for an exporter that shows no card, and throws when the exporter cannot be read.
