-- MAM reference schema v1.0. Not a complete application.
-- Apply PRAGMAs on every connection; WAL only on local storage.
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA busy_timeout = 5000;
PRAGMA synchronous = FULL;

CREATE TABLE schema_migrations (
  version INTEGER PRIMARY KEY,
  applied_at TEXT NOT NULL
);
CREATE TABLE principals (
  id TEXT PRIMARY KEY,
  os_subject TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  role TEXT NOT NULL CHECK(role IN ('admin','editor','viewer','auditor')),
  enabled INTEGER NOT NULL DEFAULT 1 CHECK(enabled IN (0,1))
);
CREATE TABLE storage_roots (
  id TEXT PRIMARY KEY,
  kind TEXT NOT NULL CHECK(kind IN
    ('originals','hires','lowres','images','pdf','previews','temp','backup')),
  absolute_path TEXT NOT NULL,
  volume_id TEXT,
  revision INTEGER NOT NULL DEFAULT 1,
  enabled INTEGER NOT NULL DEFAULT 1 CHECK(enabled IN (0,1)),
  UNIQUE(kind, absolute_path)
);
CREATE UNIQUE INDEX active_root_kind ON storage_roots(kind) WHERE enabled=1;
CREATE TABLE assets (
  id TEXT PRIMARY KEY,
  kind TEXT NOT NULL CHECK(kind IN ('video','image','pdf')),
  title TEXT NOT NULL,
  description TEXT NOT NULL DEFAULT '',
  classification TEXT NOT NULL,
  created_by TEXT NOT NULL REFERENCES principals(id),
  lifecycle TEXT NOT NULL DEFAULT 'active'
    CHECK(lifecycle IN ('active','archived','trashed','purged')),
  legal_hold INTEGER NOT NULL DEFAULT 0 CHECK(legal_hold IN (0,1)),
  trashed_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  revision INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE asset_versions (
  id TEXT PRIMARY KEY,
  asset_id TEXT NOT NULL REFERENCES assets(id),
  version_no INTEGER NOT NULL CHECK(version_no > 0),
  original_filename TEXT NOT NULL,
  source_path TEXT,
  source_sha256 TEXT NOT NULL CHECK(length(source_sha256)=64),
  source_bytes INTEGER NOT NULL CHECK(source_bytes >= 0),
  detected_mime TEXT NOT NULL,
  processing_state TEXT NOT NULL DEFAULT 'queued',
  duration_ms INTEGER CHECK(duration_ms >= 0),
  width INTEGER CHECK(width > 0),
  height INTEGER CHECK(height > 0),
  page_count INTEGER CHECK(page_count > 0),
  probe_json TEXT NOT NULL DEFAULT '{}',
  created_by TEXT NOT NULL REFERENCES principals(id),
  created_at TEXT NOT NULL,
  UNIQUE(asset_id, version_no)
);
CREATE INDEX duplicate_candidate ON asset_versions(source_sha256,source_bytes);
CREATE TABLE renditions (
  id TEXT PRIMARY KEY,
  version_id TEXT NOT NULL REFERENCES asset_versions(id),
  kind TEXT NOT NULL CHECK(kind IN ('original','hires','lowres','thumbnail','preview')),
  root_id TEXT NOT NULL REFERENCES storage_roots(id),
  relative_path TEXT NOT NULL,
  profile_id TEXT NOT NULL,
  sha256 TEXT NOT NULL CHECK(length(sha256)=64),
  bytes INTEGER NOT NULL CHECK(bytes >= 0),
  status TEXT NOT NULL CHECK(status IN ('ready','missing','quarantined','trashed')),
  created_at TEXT NOT NULL,
  UNIQUE(root_id,relative_path),
  UNIQUE(version_id,kind,profile_id)
);
CREATE TABLE processing_jobs (
  id TEXT PRIMARY KEY,
  version_id TEXT NOT NULL REFERENCES asset_versions(id),
  idempotency_key TEXT NOT NULL UNIQUE,
  step TEXT NOT NULL,
  state TEXT NOT NULL CHECK(state IN
    ('queued','running','retry_wait','succeeded','failed','cancelled','interrupted')),
  progress REAL CHECK(progress >= 0 AND progress <= 1),
  attempt INTEGER NOT NULL DEFAULT 0,
  max_attempts INTEGER NOT NULL DEFAULT 3,
  lease_owner TEXT,
  lease_until TEXT,
  cancel_requested INTEGER NOT NULL DEFAULT 0 CHECK(cancel_requested IN (0,1)),
  profile_json TEXT NOT NULL,
  root_snapshot_json TEXT NOT NULL,
  error_code TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE INDEX queue_lookup ON processing_jobs(state,created_at);
CREATE TABLE settings (
  key TEXT PRIMARY KEY,
  value_json TEXT NOT NULL,
  revision INTEGER NOT NULL DEFAULT 1,
  updated_by TEXT NOT NULL REFERENCES principals(id),
  updated_at TEXT NOT NULL
);
CREATE TABLE tags (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  normalized_name TEXT NOT NULL UNIQUE
);
CREATE TABLE asset_tags (
  asset_id TEXT NOT NULL REFERENCES assets(id),
  tag_id TEXT NOT NULL REFERENCES tags(id),
  PRIMARY KEY(asset_id,tag_id)
);
CREATE TABLE collections (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  owner_id TEXT NOT NULL REFERENCES principals(id)
);
CREATE TABLE collection_assets (
  collection_id TEXT NOT NULL REFERENCES collections(id),
  asset_id TEXT NOT NULL REFERENCES assets(id),
  PRIMARY KEY(collection_id,asset_id)
);
CREATE TABLE comments (
  id TEXT PRIMARY KEY,
  version_id TEXT NOT NULL REFERENCES asset_versions(id),
  author_id TEXT NOT NULL REFERENCES principals(id),
  body TEXT NOT NULL,
  time_ms INTEGER CHECK(time_ms >= 0),
  page_no INTEGER CHECK(page_no > 0),
  created_at TEXT NOT NULL
);
CREATE TABLE audit_events (
  seq INTEGER PRIMARY KEY AUTOINCREMENT,
  event_id TEXT NOT NULL UNIQUE,
  actor_id TEXT NOT NULL REFERENCES principals(id),
  action TEXT NOT NULL,
  target_id TEXT,
  result TEXT NOT NULL,
  details_json TEXT NOT NULL DEFAULT '{}',
  occurred_at TEXT NOT NULL,
  previous_hash TEXT,
  event_hash TEXT NOT NULL
);
CREATE INDEX asset_listing ON assets(lifecycle,created_at,id);
CREATE INDEX version_listing ON asset_versions(asset_id,version_no);
-- UTC RFC3339 timestamps, UUIDs and safe paths are validated by backend.
-- Backend also enforces state transitions, retention and authorization.
-- Audit hashes alone do not resist a privileged attacker rewriting the DB.
