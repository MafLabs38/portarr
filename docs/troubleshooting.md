# Troubleshooting

## A machine is unreachable

Portarr shows a yellow banner, "Portarr cannot read *name*", when it cannot get an answer from one of the machines it watches. Its apps then show
as **unknown**, its backups as **can't be read right now**, and its metrics as unavailable.

**This is not a failure of your apps.** Portarr only reports what it can read. A container that runs fine but sits behind a closed door
looks, from Portarr, exactly like a container that is down, so Portarr says "unknown" rather than guessing.

### What the reason means

| Reason | In plain words | Usual cause |
|---|---|---|
| `timeout` | Nobody answered in time. | The machine is off, or a **firewall silently drops** the requests (no refusal, no error: the packets just vanish). |
| `unreachable` | The connection was refused, or the machine is down. | The service is stopped, the port is wrong, or the machine is off. |
| `http-401`, `http-403` | It answered, but refused Portarr. | The credentials Portarr uses are wrong or expired. |

A `timeout` is never an application error. It is a packet lost on the way.

### The firewall case, step by step

This is the most common surprise, and the least visible one.

Portarr reaches a machine with a request that leaves **from one address**. If the machine only accepts requests from a list of addresses, and
Portarr's address is not on it, the request is dropped. Nothing says so: you only see a `timeout`.

It typically happens when Portarr **moves**:

- from your laptop or a small server to a Kubernetes cluster;
- from one cluster node to another.

Inside Kubernetes, a pod has no address visible from the rest of your network. Its outgoing requests are rewritten (SNAT) to carry the address of
**the node it runs on**. So the address to allow is the node's, not the pod's.

1. **Find the address Portarr's requests come from.** On Kubernetes: the address of the node running the Portarr pod
   (`kubectl get pod -o wide`). On Docker: the address of the Docker host.
2. **Test from where Portarr runs**, not from your own computer (your computer is allowed; Portarr may not be):
   ```sh
   # from a throwaway pod on the same node, or from the Portarr host
   curl -s -m 4 -o /dev/null -w "%{http_code}\n" http://<machine>:<port>/
   ```
   `000` means nothing came back: blocked, or machine off. Any HTTP code (even 404) means the door is open.
3. **Allow that address** in the machine's firewall, for the ports Portarr uses (Docker socket proxy, Backrest, node exporter, the Kubernetes API).
4. **If Portarr runs in Kubernetes, pin it to the node you allowed** (`nodeSelector`). If it moves to another node, its source address changes
   and the firewall blocks it again.

Portarr only ever **reads**. Allowing it never lets it start, stop or change anything.

### Quick checklist

- [ ] Is the machine on?
- [ ] Does `curl` from the Portarr host reach it (`000` = no)?
- [ ] Is the address Portarr's requests come from in the firewall's allow-list?
- [ ] Is Portarr still on the node you allowed?
- [ ] For `http-401` / `http-403`: are the credentials in Portarr's configuration still valid?
