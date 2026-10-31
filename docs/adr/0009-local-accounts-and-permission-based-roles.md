# 0009 — Local accounts and permission-based roles

- **Status**: accepted
- **Date**: 2026-10-31
- **Spec**: D-92 to D-99, §11.2

## Context

Portarr shows a real infrastructure and, since V5 and V6, lets an assistant read it and change its catalog. Until now access was "whoever can reach the port", plus shared keys for the MCP endpoint (D-70) and for catalog writes (D-85). The maintainer wants a sign-in, an administrator, simpler users who only see some pages, several kinds of roles, and a Google-style account button, and insists that RBAC must not be an afterthought: "beaucoup de choses sont liées aux rôles". Questions: how are users authenticated, what is a role, where are permissions enforced, and what happens to the shared keys, the chat and the future OIDC?

## Decision

1. **Local accounts first, with a cookie session**, as the *arr apps do (forms login). Passwords are hashed with ASP.NET Core's `PasswordHasher<T>` (PBKDF2) from `Microsoft.Extensions.Identity.Core`; the rest of ASP.NET Identity (its stores, managers and UI) is not used: the tables, the lockout and the rules are small and ours, over Dapper and FluentMigrator like everything else (ADR 0004).
2. **A role is a named set of permissions; the permissions are a closed list in code.** One authorization policy per permission, declared on each controller or action; a fallback policy requires a sign-in everywhere else, so forgetting an attribute fails closed. The four built-in roles are defined in code (not stored), so a new permission is granted to the right roles by the commit that adds it; customization is by custom roles, whose sets are stored.
3. **Permissions are claims read from the database at every request**, not copied into the cookie, so editing a role, disabling a user or changing a password applies at once.
4. **One principal for every way in**: the cookie, a per-user API key, the chat runner's token and the deprecated environment keys all produce the same kind of principal. A key never exceeds its owner's role: its permissions are the owner's *now*, narrowed by the key's scope. The environment keys stay as a `system` principal with Admin permissions for the sandbox and the agent runner.
5. **The chat is per user without teaching the runner about users**: Portarr keeps the conversation owner and filters; it mints a token per message that the runner uses as the MCP key; the MCP tools check the permissions of the page they mirror.
6. **CSRF**: a custom request header on cookie-authenticated writes, on top of `SameSite=Strict`.
7. **OIDC is step 2.** The user model carries nullable `ExternalIssuer` and `ExternalSubject`, and a new authentication scheme only has to produce the same identity.

## Alternatives considered

- **Full ASP.NET Identity**: brings its own schema, UI assumptions and migrations on top of FluentMigrator, for features (e-mail confirmation, external logins tables, two-factor) that are either not wanted yet or will come with OIDC.
- **Fixed roles only (an enum)**: simpler, but the maintainer asked for "plusieurs styles de rôles pour avoir plusieurs styles de vues"; making permissions the unit costs little more and keeps the four roles of the spec.
- **Editable built-in roles**: a new permission added later would silently miss the edited roles, or the code would have to guess; custom roles (with Duplicate) give the same freedom without that trap.
- **Permissions in the cookie or in a JWT**: stale until expiry; a disabled user or an edited role would keep working. One indexed read per request on SQLite or PostgreSQL, with a 10 second cache, is cheaper than the staleness.
- **Teaching the runner about users** (passing identities, filtering there): a second place to enforce rules. Keeping ownership in Portarr leaves the runner a dumb, replaceable process (ADR 0008).

## Consequences

- Every endpoint, route and MCP tool has a permission; the role × endpoint matrix is an integration test, and a new endpoint without a policy is a sign-in-only endpoint, visible in review.
- The shared keys are deprecated, not removed: the sandbox and the runner keep working; removing them is a later, announced step.
- Restoring a database backup restores accounts; a lost Admin password is recovered with a new database or the unattended first run on an empty users table (no recovery code path yet).
- The caches (10 s) and the in-process lock behind the last-Admin rule assume one Portarr instance per database.
