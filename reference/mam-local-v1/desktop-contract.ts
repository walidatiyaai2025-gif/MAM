// Reference contract. No native implementation or Tauri permission grants.
type UUID = string;
type Role = 'admin' | 'editor' | 'viewer' | 'auditor';
type AssetKind = 'video' | 'image' | 'pdf';
type RenditionKind = 'original' | 'hires' | 'lowres' | 'thumbnail' | 'preview';
type ErrorCode = 'ACCESS_DENIED' | 'INVALID_FILE' | 'PATH_UNAVAILABLE'
  | 'DISK_FULL' | 'DUPLICATE' | 'CANCELLED' | 'PROCESS_FAILED'
  | 'PROFILE_UNSUPPORTED' | 'CONFLICT' | 'INTEGRITY_FAILED';
type Result<T> = { ok: true; value: T; requestId: UUID }
  | { ok: false; code: ErrorCode; requestId: UUID; retryable: boolean };
interface AssetQuery {
  text?: string;
  kind?: AssetKind;
  collectionId?: UUID;
  tagIds?: UUID[];
  cursor?: string;
  limit: number; // backend: 1..100; stable created_at + id cursor
}
interface AssetSummary {
  id: UUID; title: string; kind: AssetKind; status: string;
}
interface RootProposal {
  kind: 'originals' | 'hires' | 'lowres' | 'images'
    | 'pdf' | 'previews' | 'temp' | 'backup';
  selectionToken: string; // opaque native folder-picker token
}
interface JobEvent {
  eventSeq: number;
  jobId: UUID;
  state: string;
  step: string;
  progress: number | null; // null = indeterminate; not fabricated
  updatedAt: string;
}
interface DesktopBridge {
  getSession(): Promise<Result<{ subject: string; role: Role }>>;
  selectImportFiles(): Promise<Result<{ selectionToken: string; name: string }[]>>;
  importFiles(input: {
    tokens: string[];
    mode: 'copy' | 'reference';
    classification: string;
    idempotencyKey: UUID;
  }): Promise<Result<{ jobIds: UUID[] }>>;
  listAssets(query: AssetQuery): Promise<Result<{
    items: AssetSummary[]; nextCursor?: string;
  }>>;
  getAsset(id: UUID): Promise<Result<unknown>>;
  updateMetadata(input: {
    id: UUID; expectedRevision: number; title: string;
    description: string; tagIds: UUID[];
  }): Promise<Result<{ revision: number }>>;
  getPreview(id: UUID, versionId: UUID): Promise<Result<{
    localMediaUrl: string; // scoped, read-only; no arbitrary path input
  }>>;
  selectStorageFolder(): Promise<Result<{ selectionToken: string }>>;
  validateRoot(root: RootProposal): Promise<Result<{
    writable: boolean; freeBytes: number; volumeId: string;
  }>>;
  saveRoots(input: {
    roots: RootProposal[]; expectedRevision: number;
  }): Promise<Result<{ revision: number }>>;
  relink(input: {
    renditionId: UUID; selectionToken: string;
  }): Promise<Result<void>>;
  exportAsset(input: {
    id: UUID; kind: RenditionKind; destinationToken: string;
  }): Promise<Result<void>>;
  cancelJob(id: UUID): Promise<Result<void>>;
  retryJob(id: UUID): Promise<Result<void>>;
  subscribeJobs(handler: (e: JobEvent) => void): () => void;
  listJobs(): Promise<Result<JobEvent[]>>; // recover after reconnect
  trashAsset(id: UUID): Promise<Result<void>>;
  restoreAsset(id: UUID): Promise<Result<void>>;
  createBackup(): Promise<Result<{ jobId: UUID }>>;
}
// Resolve OS identity inside backend; never trust a role passed from the UI.
// Tokens expire and are bound to a session and operation, not transferable paths.
// Add authorized collection/tag/comment endpoints with the same envelope.
