# P12 Discovery, Categories & Extracted-Text Indexing

## Scope

This runbook covers the P12 repository implementation for hierarchical categories, searchable extracted text, OCR, timestamped audio/video transcripts, reference subjects/tags, media-type permissions and Web/Windows client parity. It does **not** convert any production/site dependency in `CURRENT_PHASE.md` to PASS and does not authorize go-live.

## SQL authority

Migration `0008_p12_discovery_ai_indexing.sql` adds the authoritative SQL tables for:

- hierarchical categories (`MamCategory`) with the protected system `Uncategorized` category;
- one current category assignment per asset (`MamAssetCategory`);
- extracted/indexed text and normalized tokens (`MamAssetSearchContent`, `MamAssetSearchToken`);
- timestamp/page text segments (`MamAssetTextSegment`);
- OCR/transcript status and percentage (`MamTextExtractionStatus`);
- reference subjects, reference image assets and asset reference tags;
- role/media-kind permission rows for `Video`, `Audio`, `Image`, `Document` and `Other`.

The Central API is the only application boundary that exposes these records to clients. Desktop/Web must not receive SQL credentials or direct storage paths.

## Categories

Categories are optional and may have a parent category recursively. No fixed business depth is imposed. The service rejects self-parenting and any assignment that would make a category a descendant of itself.

Every asset without an explicit assignment resolves to the system category:

- English: `Uncategorized`
- Arabic: `غير مصنف`
- ID: `00000000-0000-0000-0000-000000000001`

The system category cannot be edited or deleted. A non-system category cannot be deleted until its child categories and assigned assets are moved.

## Search index

The P12 search surface combines:

- catalog title and curated metadata;
- OCR text;
- timestamped transcript text;
- reference tags;
- detected embedded metadata indexed by the processing worker.

Arabic/English normalization is applied before token indexing. Search responses may include a matched transcript timeline position (`StartMs`/`EndMs`) or OCR page number. Web search results can open the selected asset directly in Asset Details rather than falling back to the first catalog asset.

## OCR

Existing P12 OCR remains server-side and uses the repository's accepted OCR boundary. The Worker copies the verified OCR text result into the authoritative SQL index and stores page segments where page markers are present.

The Processing Queue reads `MamTextExtractionStatus` so OCR can expose progress/status separately from the durable processing-job state. Web and Windows Desktop both surface the extraction percentage/state.

## Timestamped transcription

Audio/video transcription uses an approved local Worker-side `whisper.cpp` compatible CLI. Required deployment configuration:

- `MAM_WHISPER_MODEL_PATH` — absolute path to the approved local model file (**required** for transcript jobs);
- `MAM_WHISPER_PATH` — executable path; defaults to `whisper-cli`;
- `MAM_WHISPER_LANGUAGE` — optional language selector; defaults to `auto`.

FFmpeg remains required and is used to prepare mono 16 kHz PCM speech audio. The Worker requests WebVTT output, parses timestamped segments, then stores transcript text and the timeline in SQL. The verified Primary original is checksum-verified before and after processing.

A missing model/tool fails the job visibly; it must never produce a false successful transcript.

## Word and text documents

Allowed office/text upload types include DOC, DOCX, RTF, TXT and ODT in addition to PDF.

- DOCX, ODT and TXT use local server-side extraction without sending bytes to an external service.
- DOCX/ODT embedded properties are captured as detected metadata and indexed.
- Legacy DOC/RTF conversion uses a deployment-pinned LibreOffice executable when those types are processed. Configure `MAM_LIBREOFFICE_PATH`; otherwise the Worker falls back to `soffice` on PATH and fails visibly if unavailable.
- PDF/image OCR continues through the OCR executor.

Detected embedded values are surfaced as detected metadata. FFprobe format/stream tags for ordinary audio/video/image inspection are also indexed. Detected values are not silently allowed to overwrite authoritative catalog fields such as title; an operator may review them before applying business metadata.

## Upload allow-list and automatic processing

Current P12 templates and both client upload surfaces allow:

- Video: MXF, MOV, MP4, MKV, AVI, WEBM, M4V
- Audio: WAV, MP3, M4A, AAC, FLAC, OGG, WMA
- Images: JPG/JPEG, PNG, TIFF/TIF, BMP, WEBP
- Documents: PDF, DOC, DOCX, RTF, TXT, ODT

After the Primary original is server-verified, clients queue the relevant processing automatically:

- Video/Audio -> inspection + timestamped transcript
- Image -> inspection + OCR
- PDF -> OCR
- DOC/DOCX/RTF/TXT/ODT -> inspection + text extraction/indexing

The server-side upload allow-list and media permission matrix remain authoritative even when a client displays allowed types. A failure while auto-queueing processing does not invalidate or delete an already verified Primary original; processing can be retried from Asset Details/Processing Queue.

## Media-type permissions

`MamRoleMediaPermission` controls these actions independently per role/media kind:

- view;
- upload;
- edit;
- process;
- download.

Initial rows preserve the existing role baseline: Administrator and CatalogEditor receive full media actions; Viewer receives view/download only. Administrators can change the matrix through the Media Permissions page/API. Upload and processing/download paths enforce the matrix server-side; hiding a button in the client is not considered authorization.

`GET /api/v1/discovery/my-media-capabilities` resolves the current authenticated user's combined role permissions without exposing the administrative matrix. The Web Upload page uses this endpoint to show the media kinds the current user is permitted to upload and narrows the file chooser accordingly. Windows Desktop is still protected by the same server enforcement even if the OS file picker displays the configured product allow-list.

## Reference library and tags

Users can create a bilingual reference subject/entity, attach image assets as reference images, and manually tag assets with those reference subjects. Tags are indexed and become searchable.

The repository does **not** automatically identify a real person from a reference photograph. Automatic person-identity/face-recognition matching is intentionally not implemented by this feature. Reference tagging remains an explicit operator action.

## Web and Windows Desktop parity

The new functional surfaces exist in both clients through the Central API boundary:

- Content Search / البحث في المحتوى
- Categories / التصنيفات
- Reference Library / مكتبة المراجع
- Media Permissions / صلاحيات أنواع الوسائط
- dashboard discovery/category metrics
- Asset Details tabs for metadata/category, transcript timeline, OCR and reference tags
- OCR/transcript Processing Queue progress/status
- expanded upload types and automatic post-upload processing

Web remains responsive and supports English LTR / Arabic RTL. Windows Desktop uses the same bilingual labels and API contracts; it does not link to `MAM.Infrastructure` or bypass the Central API.

## Deployment verification

After applying migration `0008` and starting API/Web/Worker, verify:

1. `/health/discovery` returns Ready using the target SQL database.
2. Category create/move/assign, grandchild hierarchy, cycle rejection and Uncategorized fallback work.
3. An OCR job reaches 100%, persists searchable text, and search returns the asset/page.
4. A transcript job reaches 100%, persists timestamped segments, and search returns the asset/time range.
5. A supported Word/text document exposes extracted text and embedded metadata without exposing storage paths.
6. Ordinary media inspection indexes useful embedded format/stream metadata without changing authoritative catalog fields.
7. Media permission denial returns HTTP 403 from the Central API even if a client attempts the action directly.
8. `/api/v1/discovery/my-media-capabilities` reports the current user's allowed media actions and Web Upload reflects permitted upload kinds.
9. Arabic/English UI switching does not change authoritative stored IDs or extracted source content.
10. Reference tags are searchable and remain explicitly/manual; no real-person identity is automatically inferred from reference images.
11. Web and Windows Desktop both reach search/categories/references/permissions/Asset Details through the Central API and contain no direct SQL/storage credential path.

The repository includes `p12-discovery` CI acceptance for the repository-exercisable portion of these checks. Real production DNS/TLS, identity, signing, storage independence, target hardware/UAT and authorized go-live remain P12 OWNER_LAST evidence and are not satisfied by this runbook.
