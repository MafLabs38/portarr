# Notifications

> Status: **in progress** (sandbox). The backend (categories, per-category settings, HTML mails) is built; the Settings, Notifications page follows it. Decision: D-129, [ADR 0022](../adr/0022-portarr-writes-to-the-operator-through-the-shared-notification-package-in-categories.md). Spec: F-NOT-01 to F-NOT-04, first slice.

## Need

The operator is not on the Portarr page when a backup fails or a machine goes down. Portarr already knows (the same list the MCP overview and the pages read); it must say so, by mail, without becoming noise, and the operator must be able to tune each area on its own: backups daily, maintenance off.

## The model

Notifications are **segmented into categories, one per area of the attention findings**. Each category has its own mail template and its own settings. A finding whose area is not a category (the catalog, the MCP servers) is not mailed; the Assistant sends nothing.

| Category (`id`) | Area | What it writes about | Enabled by default | Minimum level by default | Mail title |
|---|---|---|---|---|---|
| `apps` | Apps | apps that are down, degraded or unknown | yes | danger | Applications need attention |
| `infrastructure` | Infrastructure | environments that cannot be reached, machines that are not ready, metrics that cannot be read | yes | danger | Infrastructure needs attention |
| `data` | Data | backups that fail, are missing or late, tools not set up, critical data without an off-site copy | yes | warning | Backups need attention |
| `maintenance` | Maintenance | severe alerts, exposed secrets, failing CI, images behind, Dependabot, platforms out of support | **no** | danger | Maintenance needs attention |

The defaults are quiet on purpose, because little yet closes a problem once it is told. Apps, infrastructure and data: one mail, one reminder after 72 hours, then silence while it lasts, at most 2 mails a day each. Maintenance (off): even once on, one mail per episode from critical, no reminder, at most 1 a day, so a first look at the vulnerabilities is one mail, not a daily flood. Overall: at most 10 mails a day. Every value can be changed from the page.

**Why maintenance is off.** Its findings are many and noisy (every repository without Dependabot, every image a patch behind) and nothing yet closes them: the maintenance reconciliation loop does not exist. A mail the operator learns to ignore is worse than none. Turn it on at `danger` if wanted.

**Why data starts at `warning`.** A late backup is a warning, and it is exactly what the operator wants to hear about; the other categories start at `danger` because a degraded app or an unreachable metric is not worth a mail by default.

### The settings

| Setting | Scope | Meaning |
|---|---|---|
| `enabled` | global | The master switch. Off by default; nothing is sent while it is off |
| `recipient`, `from` | global | The one address written to and the one written from |
| `maxPerDay` | global | The most mails tried in 24 hours, all categories together (1 to 1000, 10 by default) |
| `enabled` | category | Whether the category writes at all |
| `minimumLevel` | category | `info`, `warning` or `danger`: findings below it stay on the pages |
| `reminderHours` | category | Hours between two reminders while it lasts (1 to 720) |
| `maxReminders` | category | Reminders after the first mail, 0 for a single mail (0 to 30) |
| `maxPerDay` | category | The most mails tried in 24 hours for the category (1 to 1000) |

Not editable from the page, set where Portarr is deployed ([deployment.md](../deployment.md)): the SMTP connection (host, port, user, password file, STARTTLS), and the delays of the policy (`ConfirmMinutes` 5, `ClearMinutes` 15, `RetryMinutes` 15) and the interval between two looks (`PassSeconds` 60).

## Scope

| In | Out (later) |
|---|---|
| Mail to **one configured recipient** through the shared `MafLabs.Notifications` package (SMTP, STARTTLS, password in a file), as HTML with a plain-text alternative | Other channels (chat, push): one class in the package |
| Four categories, each with a template and its own settings; one mail per category per pass | A periodic digest (F-NOT-03), per-stack filters, the other triggers of F-NOT-02 |
| One mail when a category has had a qualifying finding for 5 minutes, reminders per its policy, nothing when it clears | A template for the quota of a service: the next vertical |
| Caps: per category and overall, a pause after a failed mail | In-app and browser notifications (F-NOT-04) |
| Settings page data: categories, recipient, sender, caps, a test mail, the log of the mails | Editing the connection from the page (deliberately not) |

## Behaviour

- Each pass (every 60 seconds) reads the findings and, for each category that is on, keeps those at or above its minimum level. A category with at least one has an **episode**: the first sight starts it, and the mail leaves when it has lasted `ConfirmMinutes` (5). If the findings go before, nothing is sent and the episode is forgotten.
- **One mail per category**, listing every qualifying finding of the category: one line per thing concerned, `Critical - Backups are failing: media (critical)`, with the sentence of its kind (a closed vocabulary; an unknown kind is written as its code). The level of the mail (its accent and its label) is the worst finding's. Two categories with findings at the same time are two mails; three findings of one category are one.
- A mailed episode asks for a reminder every `reminderHours` of the category while it lasts, `maxReminders` at most; the last mail says that Portarr will not write again until it has gone and comes back. The subject says `(new)` or `(reminder 2 of 3)`.
- **Limit:** when the set of findings changes while an episode lasts (a second asset whose backup fails, an app that recovers), nothing extra is sent. The next reminder lists the current set and the pages show the change at once. If the category clears completely and comes back, it is a new episode and a new first mail.
- When the category clears, nothing is sent. The episode is forgotten `ClearMinutes` (15) after the last qualifying finding, so a tool that is slow to answer after a restart does not start a new episode. Turning a category off, or raising its minimum level above its findings, ends its episode the same way.
- A mail that does not leave is logged with a short reason, not counted as sent, and tried again after `RetryMinutes` (15).
- The caps count the log, so they hold across a restart: `maxPerDay` of the category and the overall `maxPerDay` (the test mail included).
- Nothing is sent while `enabled` is false, or while the settings cannot work (told once in the log, and in `problem` of the settings). A stored policy that is corrupted falls back to the category's default, with a warning in the log.

## The mails

One template per category, written once with the package's `MailTemplate` and rendered as HTML and as text (`MailLayout`): product "Portarr", a header band whose colour is the level (critical red, warning amber, information blue), the title of the category, a sentence with the count, the list of findings, the call to action ("Open the Data page of Portarr to see the details."), the reminder sentence ("If nothing changes, Portarr reminds you again in 24 hour(s)." or "Portarr will not write again about this until it has gone and comes back.") and a footer that says why the reader receives it. Every name is cleaned and HTML-encoded by the package: a hostile name cannot inject markup or a header. The test mail uses the same layout. A finding whose names are what a remote tool said (`code-source-error`) is written as its sentence alone.

## API

All under `/api/v1/notifications`, permission `settings.manage` (Admin by default). The password and the path of its file are never returned.

| Route | Body / answer |
|---|---|
| `GET /settings` | `{ enabled, recipient, from, maxPerDay, connection: { provider, host, port, userName, startTls, passwordFileConfigured }, problem, secretProblem, lastAttempt, categories: [{ id, title, description, enabled, minimumLevel, reminderHours, maxReminders, maxPerDay, defaults: { enabled, minimumLevel, reminderHours, maxReminders, maxPerDay } }] }` |
| `PUT /settings` | `{ enabled, recipient, from, maxPerDay, categories: [{ id, enabled, minimumLevel, reminderHours, maxReminders, maxPerDay }] }` (every field required; `minimumLevel` is `info`, `warning` or `danger`; a category that is not listed keeps what it has) → the settings as `GET`, or `400` with `errors` by field: `enabled`, `recipient`, `from`, `maxPerDay`, `categories` (an unknown or repeated id), `categories.<id>.<field>` (`minimumLevel`, `reminderHours`, `maxReminders`, `maxPerDay`), and `settings` when the connection or the recipient is missing while turning it on. Nothing is stored on a 400 |
| `POST /test` | no body → `{ ok, error }` (always `200`; `error` says why: off, misconfigured, the daily limit, or the mail server's failure in a sentence) |
| `GET /log` | `{ entries: [{ id, kind, category, level, reminder, sentAt, delivered, error }] }`, 50 at most, newest first. `kind` is `category` or `test`; `category` is the id (null for a test); `level` is `info`, `warning` or `danger` |

The settings are stored in the `Config` table, key `Notifications`: `{ enabled, recipient, from, maxPerDay, categories: { <id>: { enabled, minimumLevel, reminderHours, maxReminders, maxPerDay } } }`. No secret, nothing of the connection.

## UI

Settings, **Notifications** (`/settings/notifications`, `frontend/src/Settings/Notifications/`), behind `settings.manage`; the entry sits in the Settings navigation next to General, Catalog and Users & Auth. From top to bottom:

- **Mail**: the master switch, the recipient, the sender and the overall cap (mails per day, all categories).
- **Categories**: one card per category, in the order the API gives (apps, infrastructure, data, maintenance), with its title and description, a switch (an **Off** badge when it is off), the level it writes from (Info, Warning, Danger), the reminder delay, the number of reminders and its daily cap. A line shows the category's defaults, and **Reset to defaults** puts them back in the form (nothing is saved until Save).
- **One Save** for the whole page: a single `PUT` with every category. The server's field errors appear under the right field of the right card, and the sentence about the settings as a whole at the top of the form.
- **Connection** (read-only, set where Portarr is deployed), **Test**: a test mail to the saved recipient with its result inline, and **Recent mails**: when, category (or "Test mail"), level, first mail or reminder, delivered or the reason.

## Deployment

See [deployment.md](../deployment.md): `PORTARR__NOTIFICATIONS__SMTP__HOST`, `…__USERNAME`, `…__PASSWORDFILE`, the `…__POLICY__*` delays and the others. The connection is where Portarr is deployed, not in the page.

## Tests

- Unit (`Portarr.Core.Test/Notifications`): the mapping of findings to categories and the defaults, the templates (the title and the page of each category, the level accent, the escaping of a hostile name, the reminder sentence, the vocabulary), the settings (what is stored, what never is, the validation by field, a corrupted stored policy), the notifier with a fake sender, store and clock (nothing before the confirmation delay, one mail for a category with three findings, two categories two mails, reminders, the end of an episode, a restart, the caps, maintenance off by default and on), the monitor loop.
- Integration (`Portarr.Integration.Test`): the store and the migration on SQLite and PostgreSQL (`PORTARR_TEST_DATABASE`), the API with a fake sender (the categories and their defaults, the password never returned, the refusals by field, the test mail, the log, the roles).
- No real SMTP in the suite.
