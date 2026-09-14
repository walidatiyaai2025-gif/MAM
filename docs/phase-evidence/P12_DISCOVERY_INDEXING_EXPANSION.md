# P12 Discovery & Indexing Expansion — Engineering Evidence

Status: **ENGINEERING IMPLEMENTED / P12 REMAINS ACTIVE**

This record covers the repository-engineering expansion delivered through PR #37. It does not close P12 and does not convert any production/site OWNER_LAST requirement to PASS.

## Delivered scope

- hierarchical bilingual categories with protected `Uncategorized / غير مصنف`, recursive parent relationships, cycle prevention, asset assignment and category management;
- dashboard counts for categories, uncategorized assets, indexed assets, OCR, transcripts and reference subjects;
- timestamped audio/video transcription with SQL-persisted timeline segments and searchable transcript text;
- PDF/image OCR ingestion into the authoritative SQL text index with page-level segments and extraction progress;
- DOCX/ODT/TXT text extraction and indexing, with legacy DOC/RTF conversion behind a deployment-pinned LibreOffice boundary;
- embedded media/document metadata extraction and indexing without silent overwrite of authoritative catalog fields;
- text discovery across titles, curated metadata, OCR, transcripts, embedded metadata and reference tags;
- reference-subject/image library plus explicit/manual searchable tagging;
- per-role/per-media-kind view/upload/edit/process/download authorization with Central API enforcement;
- current-user media capability discovery and role-aware Web upload guidance;
- expanded allowed upload formats and automatic post-upload inspect/transcript/OCR/indexing dispatch;
- Web and Windows Desktop functional parity for search, categories, reference library, media permissions, Asset Details discovery tabs, upload types and processing progress;
- Arabic RTL and English LTR labels for the new Web/Desktop surfaces.

## Safety boundary

Reference images do not trigger automatic real-person identity recognition. Operators may create reference subjects, attach reference image assets and explicitly/manual tag media. The repository does not infer or assert that a real person in a video/image/document is a particular named person from a reference photograph.

## Architecture preserved

- SQL remains authoritative for category, index, extraction-status, reference-tag and media-permission records.
- Original media remains governed by Primary Storage and the existing protection invariant.
- Web/Desktop use the Central API and do not receive SQL credentials, storage credentials or direct Worker authority.
- Extracted text and metadata are derived/searchable information; they do not silently replace authoritative catalog fields.

## Engineering validation

PR #37 introduced a dedicated `p12-discovery` acceptance workflow. On implementation head `a548ec263f608eba3ab0fe012b1b94be66eab194`:

- `p12-discovery` run #9 / `34776169488`: **SUCCESS**;
- `p12-ocr` run #29 / `34776169497`: **SUCCESS**;
- P09 regression run #87 / `34776169500`: **SUCCESS**;
- solution build inside the above acceptance/regression paths completed successfully, including Windows-targeted Desktop compilation from Linux CI.

The initial P10 regression on that implementation head failed only because the new discovery workflow contained a repository-scanner-detectable connection-string literal. The test credential was ephemeral, but repository policy correctly treated the literal pattern as forbidden. PR #37 subsequently changed the workflow/acceptance construction so the connection string is assembled from the runtime secret without embedding the forbidden credential pattern. Full CI/P10/P11/P12 setup regressions remain mandatory on the final PR head before integration.

## Runtime dependencies

- FFmpeg/FFprobe remain required for media inspection/audio preparation.
- Timestamped transcription requires an approved local whisper.cpp-compatible executable and model (`MAM_WHISPER_PATH`, `MAM_WHISPER_MODEL_PATH`; optional `MAM_WHISPER_LANGUAGE`).
- Existing OCR runtime dependencies remain required.
- Legacy DOC/RTF extraction requires the approved LibreOffice/soffice runtime (`MAM_LIBREOFFICE_PATH` when not on PATH).

## Production/site evidence intentionally not satisfied here

The following remain governed by the existing P12 OWNER_LAST ledger and are not represented as PASS by this expansion: production DNS/TLS/network readiness, SQL production topology, real Primary/Backup topology and independence, production identity binding, signing, physical capture certification, target-site scale, physical/browser/device UAT, authorized final release hashes and go-live authorization.
