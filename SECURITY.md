# Security Policy

Portarr connects to sensitive systems (Docker sockets, Kubernetes APIs, hypervisors). We take security reports seriously.

## Reporting a vulnerability

**Do not open a public issue.** Use GitHub's [private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability) on this repository ("Security" tab → "Report a vulnerability").

Please include the affected version, reproduction steps and the impact you observed. We aim to acknowledge reports within 7 days.

## Supported versions

Until 1.0, only the latest release receives security fixes.

## Security model (summary)

- Read-only by default: Docker access goes through a filtering socket proxy; Kubernetes access uses a read-only ClusterRole.
- Write actions (restart, stop…) are disabled by default, require a dedicated role and are audited.
- Provider secrets are encrypted at rest and never returned by the API.
- Authentication through OIDC (recommended) or local accounts; API keys are scoped.

The full threat model is in the [specification](docs/SPEC.md), §11.
