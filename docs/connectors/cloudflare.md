# Cloudflare connector

Lists the **Pages projects** and **Workers** of a Cloudflare account as workloads (D-53), so sites hosted on Cloudflare show their deployment state next to their HTTP check. **Read-only**: Portarr only sends GET requests to the Cloudflare API (D-31).

## Token

Create an API token in *My Profile → API Tokens → Create Token → Custom token* with these permissions only:

| Scope | Permission | Access |
|---|---|---|
| Account | Cloudflare Pages | Read |
| Account | Workers Scripts | Read |

Limit it to the one account (*Account Resources → Include → your account*). Nothing else is needed: no zone permission, no write.

The token is **never written in the catalog**. Give it to Portarr through an environment variable (default name `CLOUDFLARE_API_TOKEN`), for example in your Compose file from a secret or an `.env` file kept out of Git:

```yaml
services:
  portarr:
    environment:
      CLOUDFLARE_API_TOKEN: ${CLOUDFLARE_API_TOKEN}
```

For local development, `dotnet user-secrets set CLOUDFLARE_API_TOKEN <token> --project src/Portarr.Host` works too: Portarr reads the variable from its configuration.

## Catalog

The account id is the 32-character hexadecimal id in the dashboard URL (`dash.cloudflare.com/<account id>`).

```yaml
environments:
  - id: cdn
    name: Cloudflare
    runtime: cloudflare
    cloudflare: { accountId: "0123456789abcdef0123456789abcdef", tokenEnv: CLOUDFLARE_API_TOKEN }   # tokenEnv: the variable name, not the token

apps:
  - id: blog
    name: Blog
    urls: [{ href: "https://blog.homelab.example" }]   # checked over HTTP by default
    workloads:
      - { runtime: cloudflare@cdn, match: { name: blog } }      # Pages project or Worker name
```

Until the token variable is set and the account id is a real one, the environment is shown as **not connected** with the reason (`token-missing`, `account-invalid`); the rest of the catalog keeps working (D-32).

## What it provides

| Workload | Endpoint used | State |
|---|---|---|
| Pages project | `GET /accounts/{id}/pages/projects` | Latest production deployment (else the live one): success → running, failure or canceled → exited, in progress → starting. Status text: deployment date and short commit. |
| Worker | `GET /accounts/{id}/workers/scripts` | Running (a listed Worker is deployed), with its last deployment date. |

Refreshed every 15 s, like every environment. A failed production deployment turns the app Down. Live usage and logs are not available for Cloudflare workloads.
