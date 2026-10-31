# GitHub connector

Scans the repositories of the operator's GitHub accounts for the **Maintenance** page (D-64): open Dependabot, code scanning and secret scanning alerts, the latest release, Dependabot pull requests waiting and the CI state of the default branch. **Read-only**: Portarr only sends GET requests; it never opens a pull request, dismisses an alert or changes a setting (D-16).

## Token

Create a **fine-grained personal access token** (*Settings → Developer settings → Fine-grained tokens*), with the resource owner set to each account to scan (one token per organization if needed, or a token of an organization member with access to the repositories), **All repositories** (or the ones to scan), and these repository permissions only:

| Permission | Access | Used for |
|---|---|---|
| Metadata | Read-only | listing repositories (mandatory) |
| Contents | Read-only | releases and tags |
| Dependabot alerts | Read-only | open Dependabot alerts |
| Code scanning alerts | Read-only | open code scanning alerts |
| Secret scanning alerts | Read-only | open secret scanning alerts |
| Actions | Read-only | the last workflow run of the default branch |
| Pull requests | Read-only | Dependabot pull requests waiting |

No write permission, no account permission. A classic token with broad scopes works but gives Portarr far more than it needs: do not use one in production.

The token is **never written in the catalog**: give it through an environment variable (default `GITHUB_TOKEN`), e.g. from a Compose secret or an `.env` file kept out of Git.

## Catalog

```yaml
sources:
  github:
    owners: [example, example-org]
    tokenEnv: GITHUB_TOKEN
    pushedWithinMonths: 6
    exclude: ["example/test-*"]

apps:
  - id: shop
    repository: example/shop        # links the app to its code (D-65)
```

See [catalog.md](../catalog.md#code-sources) for every key.

## What Portarr reads

| Request | Reading |
|---|---|
| `GET /user`, `GET /users/{owner}` | whether an owner is an organization, and whether the token belongs to a user (their private repositories are then listed) |
| `GET /orgs/{org}/repos`, `/user/repos`, `/users/{user}/repos` | the repositories, filtered by activity, forks, archived and exclusions |
| `GET /repos/{repo}/dependabot/alerts?state=open` | alerts by advisory severity; "disabled" means **off** |
| `GET /repos/{repo}/code-scanning/alerts?state=open` | alerts by security severity, quality rules apart; "no analysis found" or "must be enabled" means **off** |
| `GET /repos/{repo}/secret-scanning/alerts?state=open` | open secrets; "disabled" means **off** |
| `GET /repos/{repo}/releases/latest`, then `/tags` | latest release, else latest tag |
| `GET /repos/{repo}/pulls?state=open` | pull requests opened by `dependabot[bot]` |
| `GET /repos/{repo}/actions/runs?branch={default}&event=push&per_page=1` | the last CI run of the default branch |

## Rate limits

Every 30 minutes, 4 repositories at a time. Answers are revalidated with `If-None-Match` (ETag): an unchanged answer is a 304, which does not count against the 5,000 requests per hour. Portarr stops calling when fewer than 250 requests remain in the window, leaving room for the operator's own tools.

## Troubleshooting

| Shown | Meaning |
|---|---|
| "Dependabot off", "Code scanning off" | GitHub says the feature is disabled or was never set up on the repository: enable it in *Settings → Code security* |
| "Dependabot ?" (http-403) | the token may not read the alerts: add the permission |
| "GitHub could not be scanned: the token environment variable is not set" | `tokenEnv` names a variable that is not set |
