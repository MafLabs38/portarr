# V7 — Accounts and roles

> Status: **in progress** (sandbox). Step 1 of C2 (Authentication and roles), pulled forward from [IMPLEMENTATION_PLAN.md](../IMPLEMENTATION_PLAN.md). Decisions: D-92 to D-99, [ADR 0009](../adr/0009-local-accounts-and-permission-based-roles.md). Builds on the MCP server (V5), the writable catalog (D-83 to D-86) and the chat (V6).

## Need

> "Il faudra qu'on se connecte, il y aura tous les rôles, l'admin, le simple utilisateur qui aura le droit de voir certains trucs. On créera plusieurs styles de rôles pour avoir plusieurs styles de vues. Un petit icône en haut à droite, comme quand on se connecte à Google, un petit rond avec une image qu'on pourra setter pour l'utilisateur. Beaucoup de choses sont liées aux rôles, sinon on aura une gestion du RBAC nulle."

Until now whoever could reach Portarr could read everything, and writes, the MCP endpoint and the chat were guarded by shared keys or by nothing. This vertical is the foundation every later feature checks against.

## Goal

Login is required. Every page and every endpoint belongs to a permission; a role is a named set of permissions; every user, key and chat conversation has an owner; and everything that matters is audited.

## Scope

| In (step 1) | Out (open follow-ups, below) |
|---|---|
| Local accounts, forms login, cookie session, lockout, first-run setup (screen, or unattended from a password file) | OIDC (generic; group or claim → role mapping; "trusted header" mode): step 2 of this vertical, designed for here, not built |
| Closed list of permissions, one authorization policy each, on every `/api/v1` endpoint and `/mcp` | Observer masking (Q-46): replacing IPs and host names with aliases for a live demo |
| Built-in roles Admin, Operator, Observer, Guest; custom roles; last-Admin and own-account protection | A role's scope restricted to stacks or tags (§11.2) |
| User menu (round avatar), Profile (display name, password, avatar), per-user API keys | Two-factor sign-in; password reset by e-mail; session list and revocation |
| Settings → Users & Auth: users, roles, activity | A sidebar second level for Settings (one page with tabs meanwhile) |
| Chat and MCP tools per user: conversation ownership, per-turn token, per-tool permissions | Attachments to roles of per-environment access |
| Audit of accounts in the database; the user name in the catalog audit | Exporting the audit |

## Design

### Accounts (D-92)

- Table `Users` (migration 002, SQLite and PostgreSQL): `Username`/`UsernameNormalized` (unique, case-insensitive), `DisplayName`, `PasswordHash`, `RoleId`, `AvatarFile`, `Created`, `LastLogin`, `Disabled`, `FailedAttempts`, `LockoutEnd`, `SecurityStamp`, and nullable `ExternalIssuer`/`ExternalSubject` (uniquely indexed together) for OIDC. Timestamps are ISO 8601 text, the one format both engines sort and read alike.
- Hash: `PasswordHasher<T>` from `Microsoft.Extensions.Identity.Core` (PBKDF2), not full ASP.NET Identity. A hash computed with older parameters is replaced on the next sign-in.
- Cookie `portarr_session`: HttpOnly, `SameSite=Strict`, Secure whenever the request is HTTPS (behind a trusted proxy: as the forwarded headers say), sliding expiry (`PORTARR__AUTH__SESSIONDAYS`, 7). Behind a TLS proxy Portarr does not trust, set `PORTARR__AUTH__COOKIESECURITY=Always` (Secure, named `__Host-portarr_session`), or add the proxy to `PORTARR__TRUSTEDPROXIES`; a sign-in that crosses an untrusted proxy logs a warning saying so. The keys that sign the cookie are kept in `<data>/keys` (owner-only), so recreating the container on the same volume does not sign everyone out. The CSRF header (`X-Requested-With: portarr`) is required on `/mcp` as on `/api` for cookie sessions, a browser-session cookie unless "keep me signed in". The cookie only names the account and carries the security stamp; at every request the account is resolved again (10 s cache, cleared by every change): a disabled user, a changed password or an edited role takes effect at once.
- Sign-in (`POST /api/v1/auth/login`): one generic answer for every failure, one hash check whatever happens (a dummy hash for unknown users), `5` failures lock the account for `5` minutes, `10` attempts per minute per client, failures audited with the reason (admins see it, the caller does not).
- First run: `GET /api/v1/auth/status` says `setupRequired`; the UI then shows only the setup screen. `POST /api/v1/auth/setup` is one `INSERT … SELECT … WHERE NOT EXISTS (SELECT 1 FROM "Users")` in a transaction (`LOCK TABLE` on PostgreSQL); the integration test fires six at once and expects one 201 and five 409. Unattended: `PORTARR__AUTH__ADMINUSER` + `PORTARR__AUTH__ADMINPASSWORDFILE`, read once at startup while the table is empty.

### Roles and permissions (D-93)

| Permission | Protects |
|---|---|
| `apps.view` | Apps, stacks, icons, catalog status |
| `infrastructure.view` | Environments, workloads (details, usage, logs), infrastructure and its live stream |
| `data.view` | Data and backups |
| `maintenance.view` | Maintenance, including the manual refresh |
| `assistant.view` | The Assistant page (MCP state, knowledge base), the catalog audit |
| `chat.use` | The built-in chat |
| `catalog.write` | Reading catalog items; every catalog write, the portal identity; approving a chat change |
| `system.view` | System status, the log level, `/metrics` |
| `settings.manage` | Changing the log level |
| `users.manage` | Users, roles, activity |
| `secrets.view` | Reserved: nothing uses it yet; Admin only |

| Role | Permissions |
|---|---|
| Admin | all |
| Operator | all but `users.manage`, `settings.manage`, `secrets.view` |
| Observer | `apps.view`, `infrastructure.view`, `data.view`, `maintenance.view`, `assistant.view`, `system.view` |
| Guest | `apps.view` |

The built-in sets live in code and cannot be edited; **Duplicate** starts a custom role from one. A read-only API key keeps only the `*.view` permissions of its owner.

### Authorization and CSRF (D-94)

- Default and fallback policy: an authenticated principal. Controllers declare `[Authorize(Policy = "<permission>")]`; the principal's permissions are claims, read from the role at every request. `GET /api/v1/auth/me` returns them for the UI.
- Anonymous: `auth/status|setup|login|logout`, the portal logo, `/health/*`, `/openapi`, `/docs`, the UI files. An unauthenticated API call is a **401**, never a redirect (the cookie scheme's redirect is turned into a 401); `/mcp` answers `WWW-Authenticate: Bearer`.
- Account and key management (`profile`, `password`, `avatar`, `keys`) needs a **browser session**: an API key cannot create keys or change a password.
- CSRF: cookie-authenticated requests that change something, and the sign-in endpoints, need `X-Requested-With: portarr`; the UI's `useApiMutation` sends it. API-key requests are exempt.

### The UI

- **Gate**: nothing of the app renders until `/auth/me` answers; no session shows the login page, no user at all the setup page, a 401 anywhere (the session ended) returns to the login page (`QueryCache` hook, `resetQueries` on sign-in and sign-out).
- **Sidebar and routes**: every entry carries its permission; a role sees only its entries; a route without the permission redirects to the first page the role may see (`firstAllowedPath`); `/` is that page. The chat button asks only roles with `chat.use`. The Identity card needs `catalog.write`.
- **User menu**: the round avatar (the picture clipped to a circle, else initials on a theme color derived from the user name), then display name, user name and role, **Profile**, **API keys**, **Log out**.
- **Account** (Profile and API keys tabs): picture (png, webp, jpeg, 1 MiB, by signature; no svg; `?v=<hash>` URL), display name, password (current one required), keys (named, read or full, optional expiry, shown once).
- **Settings → Users & Auth** (`users.manage`): users (create, edit, reset password, disable, delete; no action on oneself), roles (checkboxes grouped by area; built-ins read only), activity (latest 200).

### API keys, chat and MCP (D-97, D-98)

- `ptr_…` keys: SHA-256 stored, prefix shown, last used, expiry, revoke; the permissions are the owner's role *now* narrowed by the scope; a disabled owner's keys fail. `PORTARR__APIKEY` and `PORTARR__MCP__APIKEY` keep working as the `system` principal (Admin) and log a startup warning.
- Chat: the runner stays unaware of users. Portarr records the owner of each conversation (`ChatConversations`) and filters or refuses everything else (404). With each message it mints a token (`ptc_…`, stored hashed) and sends it as `mcpToken`; the runner writes it as the MCP key of a process it starts (`agent/src/sessions.ts`); without it the configured key is used, so the protocol change is backwards compatible. The token opens `/mcp` only, and the audit's `via` comes from it.
- MCP tools: each read tool needs the permission of its page (`get_overview` and the knowledge base need all four views), every catalog tool needs `catalog.write`; the catalog audit lines carry the user.

## Security limits

- A token that leaks from the runner's per-process file (mode 600, removed when the process ends) is valid for 30 minutes after its last use and for `/mcp` only.
- The lockout is per account: an attacker can lock a known user name out for five minutes. The per-address rate limit and the generic answer are the trade-off against enumeration; an admin sees why in the activity.
- Sessions are server-validated at each request but not listed or individually revocable yet (a password change or a disable ends them all).
- Account state is kept in memory for 10 seconds between requests (and cleared by any change made through this Portarr): with several Portarr instances on one database a change can take 10 seconds to reach the others.
- The last-Admin and uniqueness checks run one at a time inside one process; they are not a distributed lock.
- Workload logs follow `infrastructure.view`, so an Observer reads them: whether they should stay hidden is part of Q-46.

## Open questions

- Q-46: what an Observer may see of the real system (logs, environment variables when they exist, internal addresses), and the masking mode.
- Per-role scope by stack or tag (§11.2).
- OIDC specifics (D-99): the mapping of groups or claims to roles, just-in-time user creation, the trusted-header mode behind an SSO proxy.
- Should a role be able to hold `secrets.view` once something uses it (audited reveal)?

## Definition of Done

- [x] Login required; setup screen and unattended first run; race-safe setup
- [x] Cookie session (HttpOnly, SameSite=Strict, Secure on HTTPS, sliding, remember me); generic failure; lockout; rate limit
- [x] Closed permission list, policy per endpoint, 401 for the unauthenticated, CSRF header
- [x] Built-in and custom roles; last Admin and own account protected
- [x] User menu, Profile, avatar, API keys, Users & Auth (users, roles, activity)
- [x] Per-user API keys scoped to the owner's role; environment keys kept as `system`, deprecated, warned
- [x] Chat conversations owned by their creator; approving needs `catalog.write`; per-turn token; per-tool permissions on MCP
- [x] Auth audit in the database; user in the catalog audit
- [x] Tests: role × endpoint matrix, setup race, lockout, key scoping, last Admin, chat ownership, CSRF; login page, user menu, filtered sidebar
- [ ] E2E journey (login, then the existing journeys) run against the e2e stack: written (`tests/e2e/global-setup.ts`), not run here (needs a Docker build)
- [ ] OIDC (step 2)
