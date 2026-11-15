-- R2 僅儲存已重新編碼的 WebP，D1 索引是讀取與關聯的依據。
CREATE TABLE portal_photos (
  kind TEXT NOT NULL CHECK (kind IN ('members', 'itineraries', 'worship')),
  record_id TEXT NOT NULL,
  slot INTEGER NOT NULL CHECK (slot BETWEEN 1 AND (CASE WHEN kind = 'members' THEN 1 ELSE 5 END)),
  object_key TEXT NOT NULL UNIQUE CHECK (length(object_key) = 48 AND object_key GLOB 'photos/*.webp'),
  state TEXT NOT NULL CHECK (state IN ('ready', 'deleting')),
  created_by TEXT NOT NULL REFERENCES portal_access(id),
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
  PRIMARY KEY(kind, record_id, slot),
  FOREIGN KEY(kind, record_id) REFERENCES portal_records(kind, id) ON DELETE RESTRICT
) STRICT;
