# Project Charter Addendum — Media Library Date & Category Trees

**Status:** ACTIVE / MERGE-LOCKED  
**Repository:** `walidatiyaai2025-gif/MAM`  
**Authoritative initiative branch:** `worker/media-library-date-category-trees`  
**Baseline main SHA:** `f9be6079fd33f9b95738ee80ce6845128c85774e`  
**Integration policy:** one final implementation PR from the authoritative initiative branch to `main`; **DO NOT MERGE** until every Full Closure Gate item in this charter is complete on one exact head and all required exact-head workflows are green.

## Purpose

Deliver one coherent media-library information architecture covering authoritative upload date, optional actual production date, structured category assignment, seeded Uncategorized behavior, and three navigable tree views in the bilingual Media Library. The work is cross-cutting across schema, SQL Server, Offline Demo SQLite, application contracts, APIs, permissions/audit, Web/Desktop surfaces where applicable, localization, setup/runtime compatibility, tests and release evidence.

Partial integration is prohibited because a schema-only, category-only, API-only or UI-only merge would leave inconsistent media semantics and could make existing media impossible to browse or curate safely.

## Non-negotiable integration and worker policy

1. `worker/media-library-date-category-trees` is the authoritative composition branch for this initiative.
2. There is exactly one final integration PR from this branch to `main`.
3. **No commit from this initiative may be merged directly or partially to `main` before the Full Closure Gate is complete.**
4. Another worker may work independently on a dedicated worker branch, but that branch must be based on/reconciled with the authoritative initiative branch and its legitimate completed work must be reviewed and integrated into `worker/media-library-date-category-trees` first.
5. Worker branches are implementation inputs only. They must never create competing partial PRs to `main` for this initiative.
6. Before selecting new work, workers must inspect the authoritative branch, its PR, active worker branches/claims and recently pushed work to avoid duplicate implementation.
7. The final initiative PR remains Draft / DO NOT MERGE until every closure item is complete and the final documentation head has fresh exact-head green CI.
8. Immediately before merge, re-read live `main`, reconcile the initiative branch without dropping legitimate work, re-run all required gates on the reconciled exact head, re-read reviews/threads/mergeability, and merge only with expected-head protection.
9. After merge, all required workflows must also pass on the resulting exact `main` merge commit before the initiative is declared CLOSED.
10. Existing P12 owner-last/site dependencies remain separate; this engineering initiative does not falsely close external production acceptance.

## Canonical data semantics

### Upload date

- Every media asset has one authoritative **Upload Date / تاريخ الرفع**.
- Upload date is assigned automatically by the server/system when the asset is created/committed to the catalog.
- Clients may display but must not choose, overwrite, spoof or patch upload date.
- For existing assets, migration must preserve history by backfilling the explicit upload date from the existing authoritative asset creation timestamp rather than using migration time.
- Upload date is immutable through normal metadata editing.
- Store authoritative timestamps in UTC; clients localize presentation only.

### Actual production date

- Every media asset may optionally have an **Actual Production Date / تاريخ الإنتاج الفعلي**.
- The field is nullable. If the user does not enter it, the stored value remains `NULL`/blank; the system must not fabricate a value from upload date, file metadata, processing time or event date.
- Users with the appropriate metadata-edit permission may set, change or clear it.
- The initial implementation treats it as a calendar date unless a future explicit requirement adds time-of-day semantics.
- Existing `EventDate` metadata remains a distinct legacy/business field and must not be silently reinterpreted or migrated into Actual Production Date without explicit evidence that the semantics are identical.

## Structured category model

- Replace free-text-only category assignment as the authoritative category relation with a structured category catalog using stable category IDs.
- Seed one permanent system category:
  - English: `Uncategorized`
  - Arabic: `غير مصنف`
- Uncategorized has a stable deterministic identity/key and is created idempotently in SQL Server and Offline Demo SQLite.
- Every asset must resolve to exactly one active category for this initiative. Existing/new assets with no valid category resolve to Uncategorized.
- Existing non-empty legacy free-text categories must be preserved by deterministic migration into structured category rows rather than discarded.
- Media Details presents category as a dropdown sourced from the authoritative category catalog, never as arbitrary free text.
- Uncategorized is the default until a user explicitly selects another category.
- The system Uncategorized category cannot be deleted or renamed into ambiguity while it is the default invariant.
- Category mutation is enforced server-side with permission checks, optimistic concurrency where applicable, authoritative reread and audit evidence.

## Media Library required views

The existing Media Library remains available and gains three first-class bilingual tabs/views.

### A. By Upload Date / حسب تاريخ الرفع

- Tree hierarchy: **Year → Month → Day → Media items**.
- Grouping uses authoritative Upload Date.
- Day nodes show all media uploaded on that calendar day, in a deterministic order.
- Media rows/cards expose useful ordered metadata without requiring expansion into raw technical fields.
- Selecting a media item navigates to that asset's real Media Details route without losing language context.

### B. By Actual Production Date / حسب تاريخ الإنتاج الفعلي

- Tree hierarchy: **Year → Month → Day → Media items** using Actual Production Date only.
- Assets with a real production date appear under the matching date hierarchy.
- Assets whose production date is null remain null in storage and are surfaced under a deliberate `No production date / بدون تاريخ إنتاج` node so they stay discoverable without fabricating a date.
- Selecting a media item navigates to Media Details.

### C. By Category / حسب التصنيف

- Tree hierarchy follows the authoritative category catalog. If category hierarchy is introduced, parent/child nesting must be preserved; otherwise categories are root nodes with their media children.
- Uncategorized is always available and contains assets that have not been explicitly classified.
- Selecting a media item navigates to Media Details.
- Authorized users may reclassify media directly from this view using:
  1. drag-and-drop onto a target category; and
  2. right-click/context-menu `Change category / تغيير التصنيف`.
- A keyboard-accessible/non-pointer alternative must exist so drag-and-drop is not the only way to perform the mutation.
- UI success is shown only after the server mutation succeeds and an authoritative reread confirms the new category.
- Failed/forbidden/conflicted mutations restore truthful UI state and do not show fake success.

## Media Details requirements

- Show Upload Date as read-only/system-assigned.
- Show Actual Production Date as an optional editable date field for authorized users, including a clear action that persists NULL.
- Show Category as an authoritative dropdown with Uncategorized selected when no explicit classification exists.
- Preserve all existing transcript/OCR/visual-search, metadata, permissions, processing and storage behavior.
- All new labels, validation, loading, empty/error states and actions have English LTR and Arabic RTL values.

## API, security and audit requirements

- Server remains authoritative for upload timestamp and category/production-date mutations.
- Existing authentication and media permission middleware remain in force.
- Direct-ID mutation attempts without view/edit permission fail closed.
- Mutation endpoints validate category existence/active state and date format/range.
- Category changes and production-date changes produce audit evidence with actor, asset, old/new value semantics without leaking secrets.
- APIs never expose server storage paths or credentials.
- Client success requires persistence plus authoritative reread.

## SQL Server + Offline Demo compatibility

- Production remains SQL Server and server-managed storage.
- Offline Demo remains SQLite, Internet-independent, and must implement equivalent media-date/category semantics.
- Migrations are idempotent and preserve existing assets/metadata.
- Demo setup clean-install and persistence/restart tests must cover seeded Uncategorized and the new fields/views.
- No requirement in this initiative may make the Demo depend on SQL Server, IIS, AD/OIDC or Internet.

## Compatibility / impact review

Before merge verify impact on:

- durable upload and asset creation;
- existing catalog snapshots and search;
- P05 metadata curation and legacy `EventDate`;
- legacy category values and facets;
- upload/media-kind permissions;
- processing, OCR/transcript, visual indexing and search;
- asset deletion and cleanup;
- category administration/default invariants;
- audit/concurrency semantics;
- SQL Server migrations/upgrades;
- Demo SQLite schema and persistence;
- Desktop/Web client boundaries;
- Media Details and Media Library route/language preservation;
- Arabic RTL + English LTR responsiveness/accessibility;
- setup/build/package flows;
- existing P09/P10/P11/P12 acceptance gates.

## Full Closure Gate — all required before merge

- [ ] Governance charter and execution/change record committed and referenced by the Draft PR.
- [ ] Initiative branch is reconciled with live `main` and no legitimate related work is stranded on another branch/PR.
- [ ] Production SQL migration adds explicit immutable upload date, nullable actual production date, structured categories and idempotent Uncategorized seed/backfill without data loss.
- [ ] Existing asset upload dates are backfilled from authoritative historical creation timestamps, not migration time.
- [ ] Existing non-empty legacy category text is preserved into structured categories; otherwise assets resolve to Uncategorized.
- [ ] Offline Demo SQLite schema/provider has equivalent fields, seed/backfill and persistence behavior.
- [ ] Application/domain/API contracts expose upload date, nullable production date and structured category identity/display safely.
- [ ] Server asset creation assigns upload date automatically and clients cannot spoof/patch it.
- [ ] Production date can be set, changed and cleared to NULL with validation, permission checks and audit.
- [ ] Category list/dropdown and assignment APIs are implemented with permission, validation, concurrency, authoritative reread and audit semantics.
- [ ] Uncategorized is seeded, stable, defaulted, protected and bilingual.
- [ ] Media Details shows read-only Upload Date, optional Actual Production Date, and category dropdown in Arabic/English.
- [ ] Media Library Upload Date tree is complete: year/month/day/media and asset-details navigation.
- [ ] Media Library Production Date tree is complete: year/month/day/media plus explicit no-production-date node without fabricated values.
- [ ] Media Library Category tree is complete and displays Uncategorized correctly.
- [ ] Category drag-and-drop mutation is complete with truthful loading/success/error/conflict states.
- [ ] Category right-click/context-menu mutation is complete.
- [ ] Keyboard/non-pointer category-change alternative is complete.
- [ ] Arabic RTL and English LTR are complete for all new tabs, nodes, fields, menus, validation and status text.
- [ ] Responsive/accessible rendering evidence exists for the new Media Library and Media Details behavior.
- [ ] Dedicated acceptance covers SQL Server schema/backfill, immutable upload date, nullable production date, category seed/default/migration, tree grouping, category mutation, permissions/audit and Demo parity.
- [ ] Existing search, OCR/transcript, visual search, processing, deletion, protection and install/runtime behavior have no unresolved critical/high regression.
- [ ] All repository-required exact-head CI/workflows are green together on the final implementation/documentation head.
- [ ] Final impact/change record identifies exact head, tests and any remaining owner-last items truthfully.
- [ ] `UNPUSHED_WORK=NONE`; no legitimate initiative implementation remains outside the authoritative branch/PR.
- [ ] Live `main`, reviews, review threads, mergeability and exact-head checks are re-read immediately before merge.
- [ ] Merge is performed normally with expected-head protection only after every checkbox above is satisfied.
- [ ] Required post-merge workflows are green on the exact resulting `main` merge commit.

## Merge authorization latch

Until every Full Closure Gate item is complete and the final exact head is green, this initiative is **NOT MERGEABLE BY POLICY** even if GitHub technically reports `mergeable=true`.

No worker, owner, automation or follow-up prompt may reinterpret a partial implementation as merge-ready. The only valid path to `main` is complete closure of this charter as one coherent integration unit.
