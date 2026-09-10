# Branding & Premium UI/UX — Diwan Al Amiri MAM

## 1. Brand status — LOCKED

The product is fully branded for **Diwan Al Amiri** across Windows, Web, installer, login, splash/loading surfaces, reports, exported manifests, operational dashboards and diagnostics.

Owner-approved direction:

- Primary organization crest/logo: **the Diwan Al Amiri crest supplied by the owner on 2026-09-10**.
- Primary UI identity: **Navy + Gold**.
- The supplied crest is used in its original artwork and original internal colors; application code must not recolor, redraw, simplify, distort or fabricate the crest.
- Navy and Gold are the application chrome/design-system colors, not a license to alter colors inside the crest.
- No vendor/demo/AI/developer branding may appear in production user-facing surfaces.

The supplied raster source is registered in `assets/branding/README.md`. Until a binary repository-upload path is available, implementations must use the registered asset path and must not substitute another logo.

## 2. Product naming

- Arabic: `نظام إدارة الأصول الإعلامية - الديوان الأميري`
- English: `Diwan Al Amiri Media Asset Management`
- Compact navigation label: `MAM`

The organization name and product name remain configuration-backed so an approved spelling change does not require code changes.

## 3. Core visual language

The UI must look governmental, premium, restrained and modern — not like a generic admin template.

### Locked application palette

The owner has locked the visual identity to **Navy + Gold**. The following implementation tokens are the baseline application palette and may only be adjusted centrally through the design-token layer:

| Token | Value | Use |
|---|---:|---|
| `brand.navy.950` | `#07182E` | deepest chrome / splash background |
| `brand.navy.900` | `#0A2342` | primary navigation / primary dark surface |
| `brand.navy.800` | `#12365A` | hover / selected dark surface |
| `brand.navy.700` | `#1A456F` | secondary dark interaction |
| `brand.gold.700` | `#99731F` | dark gold border / pressed state |
| `brand.gold.600` | `#B58A2A` | primary gold accent |
| `brand.gold.500` | `#C6A15B` | premium highlight / icon accent |
| `brand.gold.300` | `#E0C98A` | subtle highlight on navy |
| `surface.app` | `#F5F7FA` | main light application background |
| `surface.card` | `#FFFFFF` | cards/panels |
| `text.primary` | `#111827` | primary text on light surfaces |
| `text.onNavy` | `#FFFFFF` | primary text on navy |

Rules:

- Navy is the dominant institutional color.
- Gold is an accent, selection, key-action and premium-detail color; it must not flood large content surfaces.
- Gold on white is not used for small body text. Use navy/dark text for readability.
- White/light neutral content surfaces preserve media visibility and dense operational readability.
- Semantic success/warning/error colors remain distinct from branding and are never replaced by gold.
- Status meaning must never depend on color alone.

## 4. Logo usage rules

The Diwan crest is authoritative and must appear consistently.

Required applications:

- Windows application icon/installer identity using an approved derived icon asset;
- Windows splash and login;
- Web login and shell;
- navigation brand area;
- About/version screen;
- printable/exported reports and manifests where branding is enabled;
- error/maintenance pages;
- favicon/PWA identity using an approved compact derivative.

Protection rules:

- preserve aspect ratio;
- no stretching;
- no rotation;
- no recoloring;
- no glow/drop-shadow baked into the source artwork;
- no cropping of Arabic calligraphy or crest elements;
- no placing on noisy media without a controlled solid/blurred container;
- maintain a minimum clear-space token around the mark;
- use a white or navy presentation surface depending on contrast; do not invent alternate logo variants.

Any favicon/app-icon derivative must be generated from the approved owner logo and reviewed visually before release.

## 5. Experience principles

1. **Institutional premium** — formal, modern, high-confidence and restrained.
2. **Media first** — previews, thumbnails, technical status and metadata hierarchy are immediately readable.
3. **Operational clarity** — capture/upload/protection states are never ambiguous.
4. **One product** — Desktop and Web share design tokens, terminology, iconography and interaction semantics.
5. **Bilingual native** — Arabic RTL and English LTR are first-class.
6. **Responsive by composition** — layouts reflow intelligently rather than shrinking a desktop page.
7. **Accessible** — keyboard, focus, contrast and scalable text are built in.
8. **Safe actions** — destructive actions require explicit context and authorization-aware confirmation.

## 6. Shell appearance

### Windows Desktop

Default shell:

- navy top/side institutional chrome;
- Diwan crest in the brand zone;
- gold active-navigation indicator;
- white/light-neutral work canvas;
- compact high-density media and operational views;
- gold reserved for selected state, primary call-to-action accents and key separators;
- clear online/server/storage state visible without dominating the workspace.

### Web Portal

Web uses the same design tokens and visual identity as Windows while adapting composition by viewport.

Desktop/wide web may use a persistent navy navigation rail. Tablet collapses it. Mobile uses a compact header/drawer or task-appropriate bottom navigation for principal non-admin flows.

The web experience must not look like a different product from Desktop.

## 7. Information architecture

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

Navigation items are permission-aware.

## 8. Dashboard

The dashboard answers: **Is the archive healthy, what is happening now, and what requires action?**

Required cards/sections:

- total assets;
- ingested today/week;
- active captures/uploads;
- jobs processing/failed;
- Primary Storage health/capacity;
- Backup Storage health/capacity;
- assets awaiting protection;
- recent failures/alerts;
- recent ingest activity;
- quick actions: Capture, Upload, Search.

Important operational states use semantic status styling; gold must not be misused as warning/error.

## 9. Media Library

Premium asset-browser layout:

- global search;
- advanced filter drawer/panel;
- saved filters;
- grid/list toggle;
- sort;
- media-type facets;
- bulk selection for authorized operations;
- virtualization/paging strategy suitable for a large archive.

Asset cards include thumbnail, title, media type, duration/dimensions where relevant, event/source date, collection/tags summary, protection state and processing/failure indication.

At 360px web width, cards become single-column and filters move into a full-height sheet/drawer.

## 10. Asset Details

Wide composition:

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

Compact web stacks preview first, then key metadata/status, then sections/tabs.

Protection state is explicit:

- Primary: Verified / Pending / Failed
- Backup: Verified / Pending / Failed
- Checksum: Match / Pending / Mismatch
- Overall: `Protected` only when the protection invariant is satisfied

## 11. New Ingest

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

Web must not show a fake/disabled Tape Capture capability.

## 12. Tape Capture workspace — Windows

Tape Capture is a purpose-built operational workspace.

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
- post-capture verification/transfer state.

Target 1920x1080 layout:

```text
+------------------------------------------------------------------+
| Crest | Tape Capture | Device health | User | Clock              |
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

Critical capture controls are large, keyboard-operable and visually distinct from non-destructive actions.

## 13. File Upload

Desktop and Web share one lifecycle:

- drag/drop + picker;
- batch queue;
- validation;
- metadata template;
- duplicate detection;
- resumable/chunked transfer;
- pause/resume/cancel policy;
- separate transfer progress from processing/protection progress.

Example:

`Selected -> Validating -> Uploading 63% -> Verifying -> Processing -> Backup Pending -> Protected`

## 14. Processing Queue

Show job/asset, type, state, progress, worker, start/update time, attempts, error summary and permitted actions. Technical diagnostics are expandable and expose a correlation ID without exposing secrets.

## 15. Storage & Backup

Primary and Backup are separate visual entities.

Each shows:

- connection/health;
- total/used/free capacity;
- last successful write probe;
- active jobs;
- throughput where available;
- warnings;
- secret-free configuration summary.

Aggregate protection indicators:

- Protected Assets
- Backup Pending
- Backup Failed
- Checksum Mismatch

## 16. Settings UX

Settings are grouped by domain:

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

Critical changes display impact and validation. Secrets are write-only/replace and are never redisplayed in plaintext.

Branding settings may select registered approved assets/tokens; ordinary administrators must not upload arbitrary replacement government branding without an explicit privileged policy.

## 17. Responsive Web acceptance

### Mobile / compact

- single-column task composition;
- compact navigation;
- filters become sheets/drawers;
- tables adapt to cards or controlled horizontal data views;
- preview controls remain touch-safe;
- no hover-only actions.

### Tablet

- adaptive two-pane layouts where useful;
- collapsible navigation;
- overlay/side filters based on available width.

### Desktop / wide

- persistent navigation where suitable;
- multi-column dashboard;
- split asset detail;
- high-information-density operational views.

## 18. Windows responsiveness

Requirements:

- usable at 1366x768;
- optimized for 1920x1080;
- enhanced use of 1440p/4K space;
- high-DPI 100–200%;
- resizable window;
- no clipped Arabic text;
- capture preview/control usability preserved at minimum supported size.

## 19. Arabic RTL / English LTR

- full direction change, not text alignment only;
- navigation placement and directional icons are aware of RTL/LTR;
- filenames, timecode and technical IDs preserve readable technical direction;
- mixed Arabic/English metadata is explicitly tested;
- no English-only labels baked into graphics.

## 20. Accessibility

Target WCAG 2.2 AA principles on Web and equivalent Windows practices:

- keyboard reachability;
- visible focus;
- semantic labels;
- sufficient contrast;
- no color-only status meaning;
- accessible validation/errors;
- scalable text;
- reduced-motion respect.

## 21. Motion and premium polish

Use subtle functional motion only: panel transitions, upload/capture transitions, loading skeletons, toast/alert entry and progress transitions.

No excessive glass effects, decorative looping animation, neon styling or effects that reduce institutional clarity.

## 22. Empty/loading/error/degraded states

Every primary screen defines:

- loading;
- empty/no-results;
- API unreachable;
- Primary unavailable;
- Backup unavailable;
- worker unavailable;
- partial processing failure;
- authorization denied;
- stale/conflict state.

The screen must tell the user what happened and the next permitted action.

## 23. Branding surfaces acceptance checklist

Review the approved crest, Navy/Gold design system and product naming on:

- Windows installer;
- Windows app icon;
- splash/startup;
- login;
- shell/navigation;
- About/version;
- web favicon/PWA metadata;
- browser title;
- reports;
- printable/exported manifests;
- error pages;
- notifications;
- support/diagnostic bundle metadata.

## 24. Design acceptance gate

A feature is not UI-complete until checked on:

- Arabic RTL and English LTR;
- Windows 1366x768 and 1920x1080;
- Windows high-DPI;
- Web 360px, tablet and 1440px desktop;
- keyboard navigation;
- loading/empty/error states;
- permission-restricted state;
- long Arabic/English metadata values;
- Navy/Gold token compliance;
- correct, undistorted Diwan crest usage.

Desktop-only screenshots are not sufficient evidence of responsive implementation.
