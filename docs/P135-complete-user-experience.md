# P135 — Complete User Experience Convergence

Authoritative branch: `feature/p135-complete-user-experience`

This closure is intentionally one integration unit. It must not be split into parallel branches and must not merge to `main` until every item below is implemented and the repository-required gates are green on the exact branch head.

## Required behavior

1. **Environment-bound Desktop download from Dashboard**
   - Dashboard exposes a prominent bilingual `Download Desktop App / تحميل تطبيق سطح المكتب` action.
   - The Web server package contains the exact Desktop Setup produced by the same setup build under `/downloads/DiwanMAM-Desktop-Setup-current-x64.exe`.
   - Server configuration writes `/client-environment.json` with the actual environment name, API base URL and Web base URL. No secrets are exposed.
   - The browser downloads the Setup with a filename marker `__MAMENV__<scheme>__<host>__<port>`.
   - `desktop.iss` validates that marker and writes the same API base URL into `desktop.setup.json`. When the marker is present, the API wizard and stale-config preservation page are skipped; the environment-bound endpoint wins. The user does not type an API URL.

2. **Content Search modes**
   - Search visibly exposes three modes: `Text only / نص فقط`, `Text + Image / نص + صورة`, and `Image only / صورة فقط`.
   - `Text + Image` is the reference/default mode on entering Content Search.
   - Text-only continues to use the authoritative text index.
   - Image-only continues to use the visual image-search endpoint.
   - Text + Image requires both inputs and intersects authoritative text hits with visual hits by asset id, then ranks the common assets without fabricating matches.

3. **Media Library default view**
   - The default route is the premium card/grid Media Library matching the supplied reference composition: hero, advanced filters, result controls, cards/list and pagination.
   - `All Media / كل الوسائط` is the default tab.
   - `By Upload Date / حسب تاريخ الرفع`, `By Production Date / حسب تاريخ الإنتاج الفعلي`, and `By Category / حسب التصنيف` remain available as sibling tabs in the same Media Library surface.
   - Existing P133 authoritative date/category persistence and reread semantics remain unchanged.

4. **Play from here**
   - User-facing text is localized as `Play from here / تشغيل من هنا`.
   - Activating it switches to the Preview tab, seeks the real video preview to the segment time, brings the preview into view, focuses it and attempts playback.
   - If no playable video preview exists, a visible popup reports that fact instead of silently doing nothing.

5. **Asset Details tabs**
   - Asset Details is composed as horizontal sibling tabs rather than a vertical stack.
   - Tabs include Overview, Technical Metadata, Preview & Derivatives, Search & Indexing, and Organization when organization data is available.
   - Existing DOM nodes are moved rather than cloned so tested event handlers and authoritative data flows remain intact.

6. **Operational messages as popups**
   - Final success, error, denied, degraded and warning outcomes from action-state hosts are promoted to an accessible popup/alert dialog.
   - Loading and structural empty states remain inline because they are page state, not operation completion messages.
   - Popup has explicit close handling, Escape support, bilingual labels and focus.

7. **Composition constraints**
   - P135 loads after P133/P134 and before `p132-navigation-final.js`.
   - `p132-navigation-final.js` remains the last external script in `index.html` to preserve the established air-gap/navigation acceptance invariant.
   - No generic Desktop installer is offered when the server cannot provide a valid environment descriptor.

## Acceptance

The branch is mergeable only when:

- `p135-complete-ux.js` passes `node --check`.
- Setup PowerShell files parse successfully.
- Desktop Inno source contains the environment marker parser and environment-bound skip/overwrite behavior.
- Setup build compiles Desktop first, embeds the exact Desktop installer into Server/Demo Web payloads, then compiles Server and Demo.
- Static composition assertions prove the three search modes, default All Media tab, Asset Details tab surface, popup surface, Play-from-here behavior, environment descriptor and Desktop download linkage.
- Existing repository CI, P133, Visual Search, Discovery, P10/P11 and Setup Acceptance remain green on the exact PR head.
- After merge, required post-merge workflows are green on the exact `main` merge commit before this closure is called complete.
