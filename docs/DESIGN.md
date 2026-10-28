# Portarr — Design and Frontend Guidelines

> **Status**: v1, 2026-10-28. **Mandatory** for all UI code (human- or AI-written).
> **Source**: study of the Sonarr frontend (main branch, October 2026) and the Portarr v2 mockups.
> This document complements the [spec](SPEC.md) (§8 Navigation and screens). In case of conflict, the spec says **what**, this document says **how**.

---

## 0. Principle: two layers

| Layer | What it defines | Rule |
|---|---|---|
| **A. The *arr pattern** | Page structure, components, interactions, vocabulary, code conventions | **We copy how the *arr apps work.** A Sonarr user must feel at home without having to think. |
| **B. The Portarr theme** | Colors, typography, radii, shadows, illustrations | **Only through design tokens** (CSS variables). No hard-coded visual values in a component. |

Consequence: it must be possible to apply a "classic Sonarr" theme to Portarr by changing **only** the theme file. This is the compliance test for layer A.

---

## 1. Frontend code conventions (taken from Sonarr)

### 1.1 Stack

| Topic | Choice (aligned with Sonarr) |
|---|---|
| Framework | React 18 + **strict TypeScript** |
| Build | Vite |
| Server data | **TanStack Query**, through a single `useApiQuery` / `useApiMutation` wrapper (never a direct `fetch` in a component) |
| UI state | **zustand**: one store per screen for its options (`useAppsIndexOptionsStore`), persisted per user |
| Real time | **SignalR** (`@microsoft/signalr`): a single `SignalRListener` that **invalidates** the relevant TanStack queries |
| Styles | **CSS Modules** (`Foo.module.css`) + PostCSS (`postcss-nested`, `postcss-mixins`, `postcss-custom-media`), generated types (`typed-css-modules`), `classnames` |
| UI icons | **lucide-react**, only through the `Helpers/Props/icons.ts` registry |
| Charts | **Chart.js** via `react-chartjs-2` (like Sonarr), with plugins if needed (treemap) |
| Routing | React Router |
| Quality | ESLint (`simple-import-sort`, `react-hooks`, `filenames`), Prettier, Stylelint + `stylelint-order` |

### 1.2 Directory structure

```
frontend/src/
├── App/                 # App.tsx, AppRoutes.tsx, queryClient.ts, appStore.ts, ApplyTheme.tsx
├── Components/          # Shared components (Page, Table, Form, Modal, Menu, Label…)
├── Helpers/
│   ├── Hooks/           # useApiQuery, useApiMutation…
│   └── Props/           # kinds, sizes, icons, inputTypes, sortDirections, filterTypes…
├── Styles/
│   ├── Themes/themes.css        # tokens per theme ([data-theme=dark], [data-theme=light], [data-color-impaired])
│   ├── Variables/variables.css  # dimensions, fonts
│   ├── Variables/breakpoints.css# @custom-media
│   └── Mixins/                  # truncate, scroller, visuallyHidden, cover…
├── Utilities/           # translate(), formatters (bytes, cores, durations)
├── typings/             # API resource types
├── Overview/  Apps/  Infrastructure/  Data/  Activity/
├── Issues/  Maintenance/  Security/  Settings/  System/   # one folder per menu entry
```

### 1.3 File rules

- **One component per file**, PascalCase name, styles alongside: `AppPoster.tsx` + `AppPoster.module.css`.
- Data hooks live next to their feature: `Apps/useApps.ts`, `System/Status/useSystemStatus.ts`.
- No anonymous default exports; use `export default function AppPoster(…)`.
- Absolute imports from `src` (`import Label from 'Components/Label'`).

### 1.4 Prop vocabulary (identical to the *arr apps)

- **`kind`**: `default` · `primary` · `success` · `warning` · `danger` · `info` · `inverse` · `disabled` · `purple` · `pink`.
- **`size`**: `extraSmall` · `small` · `medium` · `large` · `extraLarge` · `extraExtraLarge`.
- Constants come from `Helpers/Props` (`kinds.DANGER`, `sizes.SMALL`), never string literals.

### 1.5 Text and translation

- **No hard-coded UI text**: always `translate('KeyName')`.
- Keys live on the backend (localization files `en.json`, `fr.json`), as in the *arr apps. English is the source language. Keys are English PascalCase (`UnexpectedStop`, `ConnectPrometheusToSeeHistory`).

---

## 2. Page anatomy (mandatory)

Every page follows the *arr structure exactly:

```
Page
├── PageHeader            (56 px: logo, global search Ctrl+K, global health, issue counter, user menu)
├── PageSidebar           (220 px: two-level menu, collapsible, counter badge per entry)
└── PageContent title="…" (browser tab title)
    ├── PageToolbar       (52 px: actions on the left · PageToolbarSpacer · view/sort/filter options on the right)
    ├── PageContentBody   (32 px padding, 16 px below 768 px)
    │   ├── PageHeading scope="System" title="Status"
    │   └── … sections (FieldSet / SectionHeading / Card)
    ├── PageJumpBar       (long lists: jump by letter)
    └── PageContentFooter (counters, legend, selection actions)
```

```tsx
export default function Status() {
  return (
    <PageContent title={translate('Status')}>
      <PageContentBody>
        <PageHeading scope={translate('System')} title={translate('Status')} />
        <Health />
        <DiskSpace />
        <About />
      </PageContentBody>
    </PageContent>
  );
}
```

Forbidden: a page without `PageContent`, a custom toolbar, primary actions outside the `PageToolbar`.

---

## 3. Screen patterns

### 3.1 "Index" screen (Apps, Workloads, Environments, Databases…)

Follows Sonarr's **Series Index**:

- **Three views** selectable from the toolbar's *View* menu: **Posters**, **Overview**, **Table**.
- **Sort** and **Filter** menus in the toolbar; savable *custom filters* (FilterBuilder).
- Per-view **Options** in a modal (poster size, displayed fields, table columns).
- **Selection mode** (*Select* button) + `SelectFooter` for bulk actions.
- **Footer** with counters and the status legend.
- **Jump bar** when the list is longer than one screen.
- View, sort and filter options are persisted per user (zustand store + API).

### 3.2 "Detail" screen (an App, a Workload, an Environment)

- Header: `AppIcon` (large), name, `StatusLabel`, links (URLs, code, deployment), tags.
- Toolbar: actions (Refresh, Maintenance, Live logs, Edit, Delete).
- Tabbed content (`react-tabs`): Overview · Logs · Metrics · Dependencies · Security · Updates · Events.

### 3.3 Provider settings (Settings → Providers, Notifications…)

Follows **Settings → Indexers / Download Clients / Connect**:

- Grid of **cards**, one per configured instance, plus a **"+"** card.
- "+" opens **AddProviderModal**: list of available implementations, with presets.
- Choosing an implementation opens **EditProviderModal**, whose form is **generated from the backend schema** (`[FieldDefinition]` → `ProviderFieldFormGroup`): field types, `helpText`, `helpLink`, advanced fields hidden behind **Show Advanced**.
- Modal footer buttons: **Delete** (on the left) · **Test** · **Cancel** · **Save**.
- Validation errors and warnings (FluentValidation on the backend) are displayed under each field.

### 3.4 System

Identical to the *arr apps: **Status** (Health, Disk Space, About, More Info) · **Tasks** (scheduled + queue) · **Backup** · **Updates** · **Events** · **Log Files**.

### 3.5 Modals

`Modal` → `ModalContent` → `ModalHeader` / `ModalBody` (30 px padding) / `ModalFooter`. Focus trapped (`react-focus-lock`), closes on Escape, primary button on the right.

---

## 4. Component catalog

### 4.1 Taken from the *arr apps (same name, same API)

`Alert` · `Card` · `CircularProgressBar` · `DescriptionList` · `FieldSet` · `Form*` (`FormRow`, `FormLabel`, `FormInput`, `FormInputHelpText`, `ProviderFieldFormGroup`, typed inputs) · `Icon` · `Label` · `Link` · `LoadingIndicator` · `Menu` · `Modal*` · `PageMessage` · `ProgressBar` (5 / 15 / 20 px) · `Scroller` · `SectionHeading` · `SpinnerIcon` · `StatusIndicator` · `Table` (sorting, column options, selection) · `TagList` · `Tooltip` · `keyboardShortcuts`.

### 4.2 Portarr-specific

| Component | Role | Notes |
|---|---|---|
| `AppIcon` | An app's logo (Jellyfin, Grafana…) | Resolved from *dashboard-icons* by name or image, can be overridden, falls back to a letter on a colored dot. Not to be confused with `Icon` (lucide pictograms). |
| `StatusLabel` | An entity's status | `Label` with the mapping from §5; always **dot + text**. |
| `UptimeBar` | A check's history as bars | 60 to 90 segments, tooltip per segment. |
| `Sparkline` | Mini line chart | No axes, optional deployment markers. |
| `CoreBars` | CPU per core | Vertical bars, color by threshold. |
| `SegmentedBar` | Memory or disk broken down by consumer | Legend required. |
| `ResourceTreemap` | "Who uses what" (F-MET-05) | Click = selection, detail panel. |
| `MetricCard` | Value + unit + trend + source | "via Prometheus" badge. |
| `LogViewer` | Live or historical logs | Monospace font, colors by level, pause, search, multiple sources. |
| `SourceBadge` | Origin of a piece of data or an app | "via Docker API", "from labels", "Terraform". |
| `CapabilityHint` | Unavailable feature | "Connect Prometheus to see history" + link to Settings → Providers. |

---

## 5. Statuses: mandatory mapping

| Portarr status (spec §5) | `kind` | lucide icon | Text |
|---|---|---|---|
| `Healthy` | `success` | `CircleCheck` | Healthy |
| `Degraded` | `warning` | `TriangleAlert` | Degraded |
| `Down` | `danger` | `CircleX` | Down |
| `Unknown` | `disabled` | `CircleHelp` | Unknown |
| `Maintenance` | `info` | `Wrench` | Maintenance |
| `Paused` | `inverse` | `CirclePause` | Paused |

- Icon names are indicative (lucide sometimes renames its icons): the reference is the `Helpers/Props/icons.ts` registry.
- A status is **never** conveyed by color alone: dot or icon **plus** text.
- The **`[data-color-impaired]`** mode (present in Sonarr) is mandatory: it replaces red/green pairs with distinguishable colors (blue/orange) and strengthens icons.

---

## 6. Portarr theme (layer B)

### 6.1 Rules

- **Same token names as Sonarr** (`--paper`, `--paperCard`, `--hairline`, `--ink`, `--inkSoft`, `--inkMuted`, `--accent`, `--successColor`, `--warningColor`, `--dangerColor`, `--infoColor`…). Components taken from the *arr apps remain compatible.
- Additional tokens are prefixed by their role: `--displayFontFamily`, `--radius*`, `--shadow*`, `--border*`.
- **Dark theme by default**, light theme available, selected in Settings → UI (`data-theme` on `<html>`).
- Fonts are **self-hosted** in the image (offline homelab, no requests to a third-party CDN).

### 6.2 Art direction: midnight-blue "cartoon"

Identity approved on the v2 mockups:
- thick cards with very rounded corners and a **hard drop shadow** (sticker effect, no blur);
- rounded fonts;
- bright colors on a midnight-blue background;
- a pirate nod (*arr*): **porthole** logo, "Ahoy" on the home screen and in empty states.

### 6.3 Tokens (dark theme)

```css
[data-theme=dark] {
  /* Surfaces */
  --paper: #121630;            /* page background */
  --paperWarm: #161b3b;        /* sidebar, header */
  --paperCard: #1d2245;        /* cards */
  --paperSunken: #0d1026;      /* sunken areas: logs, inputs */
  --hairline: #2c3366;         /* borders */
  --hairlineStrong: #3a4280;

  /* Text */
  --ink: #eef0ff;
  --inkSoft: #c3c8ee;
  --inkMuted: #a9b1d6;         /* ≥ 4.5:1 on --paperCard */
  --inkOnAccent: #1a1d3a;      /* text placed on a bright color */

  /* Accent and brand */
  --accent: #ffc145;           /* pirate gold: primary action, active element */
  --accentDeep: #b9851b;       /* accent button shadow */
  --secondary: #3ee0c5;        /* turquoise: secondary action, porthole */

  /* Statuses */
  --successColor: #5eea9b;
  --warningColor: #ff9f43;     /* orange, distinct from the gold accent */
  --dangerColor: #ff6b81;
  --infoColor: #7aa8ff;
  --disabledColor: #a9b1d6;
  --purple: #b18cff;           /* VM, desktop */
  --pink: #ff8fa0;

  /* Shape */
  --radiusSmall: 10px;   /* labels, small buttons */
  --radiusMedium: 14px;  /* buttons, inputs, menu items */
  --radiusLarge: 22px;   /* cards */
  --radiusPill: 999px;
  --borderWidth: 2px;
  --shadowCard: 0 6px 0 #0b0e22;
  --shadowButton: 0 4px 0 var(--accentDeep);
  --shadowPressed: 0 1px 0 var(--accentDeep);   /* :active, translateY(3px) */
}
```

> Deviation from the v2 mockups: `--warningColor` changes from gold to **orange** `#ff9f43`, so that a warning is never confused with the accent.

The light theme uses the same structure. Initial values: `--paper #f4f2ff`, `--paperCard #ffffff`, `--hairline #d9d6f5`, `--ink #1a1d3a`, shadows `#c9c5ef`. **To be validated in a mockup.**

### 6.4 Typography

| Token | Font | Usage |
|---|---|---|
| `--displayFontFamily` | **Fredoka** 600–700 | Page titles, card titles, large figures |
| `--defaultFontFamily` | **Nunito** 500–800 | Body text, UI |
| `--monoSpaceFontFamily` | **JetBrains Mono** 500 | Logs, versions, technical values, timestamps |

Scale: 12 (meta) · 14 (body) · 17–20 (card titles) · 22 (section titles) · 30 (page title).

### 6.5 Dimensions (identical to the *arr apps)

`--headerHeight: 56px` · `--sidebarWidth: 220px` · `--toolbarHeight: 52px` · `--pageContentBodyPadding: 32px` (16 px on small screens) · `--modalBodyPadding: 30px` · breakpoints `480 / 768 / 992 / 1200 / 1450` as `@custom-media`.

---

## 7. Data visualization

- **Always show the unit and the source**: "0.42 core · via Prometheus".
- Consistent units: memory and disk in human-readable bytes (MB, GB, TB); CPU in **cores** and as a **% of the reference capacity**, stating which one (host, container limit, cluster allocatable).
- Series colors: stable per entity (a stack or a namespace keeps its color across all views) and chosen outside the status palette.
- Vertical markers for **deployments** and **events** (unexpected stops).
- Every chart offers a **deep link to Grafana** with the query pre-filled.
- Unavailable data: show a `CapabilityHint`, never an empty chart without explanation.

---

## 8. Tone and content

- Short sentences, imperative mood for actions ("Connect Prometheus").
- Pirate nods **only** on the home screen and in empty states ("Ahoy! No issues on the horizon."), **never** in errors or alerts.
- No unnecessary jargon in titles; technical details go in tooltips and detail panels.

---

## 9. Accessibility and responsive design

- AA contrast (4.5:1 for text, including secondary text).
- Real elements: `<button>`, `<a href>`, `<input>` + `<label>`; `aria-label` on icon buttons; `role="img"` + `aria-label` on charts.
- Full keyboard navigation, documented shortcuts (*Keyboard shortcuts* modal, as in the *arr apps).
- Below 768 px: the sidebar collapses into a menu, the toolbar overflows into an *Overflow* menu (Sonarr component), tables scroll horizontally.
- `prefers-reduced-motion` is respected: no decorative animation.

---

## 10. Pull request UI checklist

- [ ] The page follows the anatomy in §2 and the screen pattern in §3.
- [ ] Existing shared components are reused; any new shared component is added to §4.
- [ ] No hard-coded colors, fonts, radii or shadows: tokens only.
- [ ] Statuses follow the mapping in §5, including in `data-color-impaired` mode.
- [ ] All text goes through `translate()`, with `en` and `fr` keys.
- [ ] The page has been checked in dark and light themes, and at 390 px wide.
- [ ] Component tests and the accessibility audit (axe) pass (see [TESTING.md](TESTING.md)).
- [ ] Screenshots are attached to the PR.
