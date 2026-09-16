# P12 Visual Segment Intelligence & Image Search — Execution Ledger

**State:** IN PROGRESS / MERGE LOCKED  
**Branch:** `worker/p12-visual-segment-image-search`  
**Charter:** `docs/PROJECT_CHARTER_VISUAL_SEGMENT_SEARCH.md`  
**ADR:** `docs/adr/0006-visual-segment-indexing-image-search.md`

## Live rule

This ledger is intentionally committed before implementation so concurrent workers can discover the active initiative. Exactly one PR owns the entire scope. No partial subset may merge to `main`.

## Implementation checklist

- [x] Charter committed.
- [x] ADR committed.
- [ ] SQL Server migration.
- [ ] Demo SQLite parity.
- [ ] Application contracts.
- [ ] Visual provider abstraction + local provider.
- [ ] SQL persistence/search implementation.
- [ ] Segment thumbnail/index processing path.
- [ ] API endpoints and permission enforcement.
- [ ] Web proxy boundary.
- [ ] Asset Details transcript + visual segment UI.
- [ ] Search by image UI.
- [ ] Arabic RTL / English LTR verification.
- [ ] Deletion/reprocessing/idempotency compatibility.
- [ ] Dedicated acceptance tests/workflow.
- [ ] Existing regression suites green.
- [ ] Exact-head reconciliation with current main.
- [ ] Final impact review.
- [ ] Merge to main.
- [ ] Post-merge exact-main validation.

`UNPUSHED_WORK=NONE`
