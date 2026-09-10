# Branding & Premium UI/UX — Diwan Al Amiri MAM

## 1. Brand ownership

The application is fully branded for **Diwan Al Amiri** across Windows, Web, installer, login, splash/loading surfaces, reports, exported manifests and operational dashboards.

No vendor/demo/AI/developer branding may appear in production user-facing surfaces.

Official visual assets must be owner-provided/approved. Until supplied, development uses clearly marked neutral placeholders; developers must not fabricate an official crest, seal or government logo.

## 2. Product naming

Working names until final approval:

- Arabic: `نظام إدارة الأصول الإعلامية - الديوان الأميري`
- English: `Diwan Al Amiri Media Asset Management`
- Compact navigation label: `MAM`

Final Arabic/English spelling is a branding setting and must be confirmed before release candidate.

## 3. Experience principles

1. **Institutional premium** — restrained, formal, modern and high-confidence rather than decorative.
2. **Media first** — previews, thumbnails, technical status and metadata hierarchy are immediately readable.
3. **Operational clarity** — capture/upload/protection states are never ambiguous.
4. **One product** — Desktop and Web share design tokens, terminology, iconography and interaction semantics.
5. **Bilingual native** — Arabic RTL and English LTR are first-class, not translated afterthoughts.
6. **Responsive by composition** — layouts reflow intelligently; no miniature desktop page on mobile.
7. **Accessible** — keyboard, focus, contrast, scalable text and status semantics are built in.
8. **Safe actions** — destructive operations have explicit context and authorization-aware confirmation.

## 4. Design tokens

Centralize tokens for both platforms:

- brand primary / secondary / accent;
- app background / surface / elevated surface;
- text primary / secondary / disabled;
- border/divider;
- semantic success / warning / danger / information;
- focus ring;
- spacing scale;
- corner-radius scale;
- elevation/shadow scale;
- typography scale;
- animation durations/easing;
- control heights and density modes.

Exact brand colors and fonts are not guessed. Tokens receive approved values once official assets/palette are supplied.

## 5. Information architecture

Primary navigation:

1. **Dashboard**
2. **Media Library**
3. **New Ingest**
   - Capture from Tape — Windows only
   - Upload Files — Windows + Web
4. **Collections**
5. **Processing Queue**
6. **Reports**
7. **Administration** — permission controlled
   - Users & Roles
   - Metadata Dictionaries
   - Capture Devices / Workstations
   - Storage & Backup
   - Processing Profiles
   - Audit Log
   - System Health
   - Settings

Navigation items must be permission-aware.

## 6. Dashboard

The dashboard answers: **Is the archive healthy, and what requires action?**

Cards/sections:
- total assets;
- assets ingested today/week;
- active captures/uploads;
- jobs processing/failed;
- Primary Storage capacity/health;
- Backup Storage capacity/health;
- assets awaiting protection;
- recent failures/alerts;
- recent ingest activity;
- quick actions: Capture, Upload, Search.

Desktop/wide web uses a responsive grid. Compact web stacks priority cards with alerts first.

## 7. Media Library

Premium asset-browser layout:

- top search bar;
- advanced filter drawer/panel;
- saved filters;
- grid/list toggle;
- sort;
- asset-count/paging/infinite-window strategy;
- media-type tabs/chips;
- bulk selection for authorized operations.

Asset card baseline:
- thumbnail/preview marker;
- title;
- media type;
- duration/dimensions where relevant;
- event/source date;
- collection/tags summary;
- protection badge;
- processing/failure indicator.

Grid density adapts to viewport. On 360px mobile web, cards become single-column and filters move into a full-height sheet/drawer.

## 8. Asset details

Wide layout:

```text
+--------------------------------------------------------------+
| Breadcrumbs / title / actions                                |
+-------------------------------+------------------------------+
|                               | Overview / Metadata          |
|       MEDIA PREVIEW           | Technical                    |
|                               | Storage & Protection         |
|                               | Versions / Derivatives       |
+-------------------------------+ Activity / Notes             |
| timeline / markers / controls |                              |
+-------------------------------+------------------------------+
```

Compact web stacks preview first, then key metadata/status, then tabs/sections.

Protection state must be explicit:

- Primary: Verified / Pending / Failed
- Backup: Verified / Pending / Failed
- Checksum: Match / Pending / Mismatch
- Overall: Protected only when invariant is satisfied

## 9. New Ingest landing

Windows:

```text
New Ingest

[ Capture from Tape ]    [ Upload Existing Files ]
 Professional device      Video / Image / Audio / PDF
 ingest                    multi-file ingest
```

Web:

```text
New Ingest

[ Upload Existing Files ]
```

No disabled fake Tape Capture card on web; capability is platform-aware.

## 10. Tape Capture workspace — Windows

This is a purpose-built operational screen, not a generic form.

Required areas:
- capture device + input profile;
- source signal state;
- live preview;
- audio meters;
- timecode;
- recording duration;
- dropped frames/error counter;
- cache free-space indicator;
- tape ID/barcode;
- required metadata;
- prominent Record / Stop controls;
- ingest state and transfer state after capture.

Suggested layout at 1920x1080:

```text
+------------------------------------------------------------------+
| Diwan branding | Tape Capture | Device health | User | Clock     |
+------------------------------+-----------------------------------+
|                              | Tape ID / metadata                 |
|       LIVE PREVIEW           | Device / Input / Format           |
|                              | Audio meters                       |
|                              | Cache / network readiness          |
+------------------------------+-----------------------------------+
| TC 01:23:45:12  REC 00:42:16 | Dropped: 0                        |
| [ RECORD ] [ STOP ]           |                                  |
+------------------------------------------------------------------+
| Capture -> Verify -> Upload -> Primary -> Backup -> Protected     |
+------------------------------------------------------------------+
```

Critical capture controls are large, keyboard-operable and impossible to confuse with non-destructive buttons.

## 11. File Upload workspace

Desktop and web share terminology and lifecycle:

- drag/drop + file picker;
- batch queue;
- per-file validation;
- metadata template;
- duplicate detection;
- resumable progress;
- pause/resume/cancel policy;
- clear separation of local transfer progress from server processing/protection progress.

Example states:
`Selected -> Validating -> Uploading 63% -> Verifying -> Processing -> Backup Pending -> Protected`

## 12. Processing Queue

Purpose: operational transparency.

Columns/cards:
- job/asset;
- type: probe/proxy/thumbnail/backup/integrity;
- state;
- progress;
- worker;
- started/updated;
- attempts;
- error summary;
- permitted actions: retry/cancel/details.

Errors use human-readable summary plus expandable technical diagnostics/correlation ID.

## 13. Storage & Backup screen

Show Primary and Backup as separate entities, never one generic storage card.

For each:
- connection/health;
- total/used/free capacity;
- last successful write probe;
- active jobs;
- throughput where available;
- warnings;
- configuration summary without secrets.

Also show:
- `Protected Assets`
- `Backup Pending`
- `Backup Failed`
- `Checksum Mismatch`

## 14. Administration settings UX

Settings are grouped by domain rather than one giant page:

- General / Localization
- Branding
- Authentication
- Users & Roles
- Metadata
- Ingest & Upload
- Capture Stations
- Processing Profiles
- Primary Storage
- Backup Storage
- Retention
- Audit & Logs
- Notifications
- System / Database / Backup

Critical changes show impact, validation result and required permissions. Secrets use write-only/replace controls and are never redisplayed in plaintext.

## 15. Responsive rules — Web

### Mobile / compact
- bottom navigation or compact top navigation for principal user tasks;
- admin areas may use drawer navigation;
- single-column composition;
- filter sidebars become sheets/drawers;
- tables become cards or horizontally managed data views;
- preview controls remain touch-safe;
- no hover-only action.

### Tablet
- adaptive two-pane views where useful;
- collapsible navigation;
- filters can be overlay or side panel depending on width.

### Desktop / wide
- persistent navigation rail/sidebar;
- multi-column dashboard;
- split asset detail;
- high-information-density operational tables with user-controlled density.

## 16. Windows responsiveness

Desktop layout uses adaptive panels and minimum constraints rather than fixed pixel positioning.

Requirements:
- usable at 1366x768;
- optimized for 1920x1080;
- enhanced use of 1440p/4K space without merely scaling everything up;
- high-DPI 100–200%;
- resizable window;
- no clipped Arabic text;
- capture screen protects preview/control usability at minimum supported size.

## 17. Arabic RTL / English LTR

- Full layout direction changes, not text alignment only.
- Navigation placement, chevrons, progress semantics and form alignment are direction-aware.
- Numbers, timecode, filenames and technical identifiers preserve readable technical direction.
- Mixed Arabic/English asset metadata is tested explicitly.
- No embedded English-only text in images.

## 18. Accessibility

Target WCAG 2.2 AA principles for web and equivalent Windows accessibility practices:
- keyboard reachability;
- visible focus;
- semantic labels;
- sufficient contrast;
- do not rely on color alone for status;
- accessible validation/errors;
- scalable text;
- reduced-motion respect where applicable.

## 19. Motion and polish

Use subtle functional motion only:
- panel transitions;
- upload/capture state transitions;
- skeleton/loading states;
- toast/alert entry;
- progress transitions.

No excessive glass effects, looping decorative animation or visual treatment that reduces legibility on operational screens.

## 20. Empty, loading, error and degraded states

Every primary screen must define:
- loading;
- empty library/no search results;
- offline/API unreachable;
- Primary unavailable;
- Backup unavailable;
- worker unavailable;
- partial processing failure;
- authorization denied;
- stale/conflict state.

Users must see what happened and the next permitted action.

## 21. Branding surfaces checklist

Diwan branding must be reviewed on:
- Windows installer;
- Windows app icon;
- splash/startup;
- login;
- shell/navigation;
- about/version;
- web favicon/PWA metadata if used;
- login/browser title;
- reports;
- printable/exported manifests;
- error pages;
- notifications;
- support/diagnostic bundle metadata.

## 22. Design acceptance gate

A feature is not UI-complete until it has been checked on:
- Arabic RTL and English LTR;
- Windows 1366x768 and 1920x1080;
- Windows high-DPI;
- Web 360px, tablet, 1440px desktop;
- keyboard navigation;
- loading/empty/error states;
- permission-restricted state;
- long Arabic/English metadata values.

Pixel-perfect desktop-only screenshots are not sufficient evidence of a responsive implementation.
