-- 核准資格由維運流程建立；Access 僅能綁定已核准且仍啟用的帳號。
CREATE TABLE portal_access (
  id TEXT NOT NULL PRIMARY KEY CHECK (
    length(id) = 36 AND length(replace(id, '-', '')) = 32 AND
    substr(id, 9, 1) = '-' AND substr(id, 14, 1) = '-' AND
    substr(id, 19, 1) = '-' AND substr(id, 24, 1) = '-' AND
    replace(id, '-', '') NOT GLOB '*[^0-9a-f]*'
  ),
  email TEXT NOT NULL UNIQUE COLLATE NOCASE CHECK (
    email COLLATE BINARY = lower(trim(email)) AND
    length(email) BETWEEN 3 AND 254 AND
    instr(email, '@') BETWEEN 2 AND length(email) - 1 AND
    instr(substr(email, instr(email, '@') + 1), '@') = 0 AND
    email NOT GLOB '*[^!-~]*' AND instr(email, char(0)) = 0
  ),
  access_subject TEXT UNIQUE CHECK (access_subject IS NULL OR length(access_subject) BETWEEN 1 AND 256),
  active INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
  role TEXT NOT NULL CHECK (role IN ('Admin', 'User'))
) STRICT;

CREATE TABLE portal_records (
  id TEXT NOT NULL PRIMARY KEY CHECK (
    length(id) = 36 AND length(replace(id, '-', '')) = 32 AND
    substr(id, 9, 1) = '-' AND substr(id, 14, 1) = '-' AND
    substr(id, 19, 1) = '-' AND substr(id, 24, 1) = '-' AND
    replace(id, '-', '') NOT GLOB '*[^0-9a-f]*'
  ),
  kind TEXT NOT NULL CHECK (kind IN ('members', 'itineraries', 'worship', 'events')),
  active INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
  version INTEGER NOT NULL DEFAULT 1 CHECK (version BETWEEN 1 AND 9007199254740991),
  payload TEXT NOT NULL CHECK (
    length(CAST(payload AS BLOB)) <= 131072 AND json_valid(payload) = 1 AND json_type(payload) = 'object'
  ),
  created_by TEXT NOT NULL REFERENCES portal_access (id) ON DELETE RESTRICT,
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
  updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),

  -- 搜尋文字以 Unicode 小寫合併標題、地點、描述及標籤；其餘索引從資料內容生成。
  search_text TEXT,
  member_position INTEGER GENERATED ALWAYS AS (
    CASE WHEN kind = 'members' THEN json_extract(payload, '$.sortOrder') END
  ) STORED,
  itinerary_date TEXT GENERATED ALWAYS AS (
    CASE WHEN kind = 'itineraries' THEN json_extract(payload, '$.date') END
  ) STORED,
  event_start TEXT GENERATED ALWAYS AS (
    CASE WHEN kind = 'events' THEN json_extract(payload, '$.startDate') END
  ) STORED,
  event_overlap_end TEXT GENERATED ALWAYS AS (
    CASE WHEN kind = 'events' THEN coalesce(json_extract(payload, '$.endDate'), event_start) END
  ) STORED,
  repeat_from TEXT GENERATED ALWAYS AS (
    CASE WHEN kind = 'events' AND json_type(payload, '$.recurrenceRule') = 'object' THEN max(
      CASE WHEN json_extract(payload, '$.recurrenceRule.type') IN ('LunarYearly', 'LunarMonthly')
        THEN printf('%04d-01-01T00:00:00.000Z', json_extract(payload, '$.lunarDate.year'))
        ELSE event_start
      END,
      coalesce(json_extract(payload, '$.recurrenceRule.effectiveFrom'), '1900-01-01T00:00:00.000Z')
    ) END
  ) STORED,
  repeat_until TEXT GENERATED ALWAYS AS (
    CASE WHEN repeat_from IS NOT NULL THEN
      coalesce(substr(json_extract(payload, '$.recurrenceRule.effectiveUntil'), 1, 10) || 'T23:59:59.999Z',
        '2100-12-31T23:59:59.999Z')
    END
  ) STORED,

  UNIQUE (kind, id),
  CHECK (kind = 'members' OR active = 1),
  CHECK (kind <> 'members' OR (
    coalesce(json_type(payload, '$.isActive') IN ('true', 'false'), 0) AND
    json_extract(payload, '$.isActive') = active AND
    coalesce(json_type(payload, '$.sortOrder') = 'integer', 0) AND
    member_position BETWEEN -1000000 AND 1000000
  )),
  CHECK ((kind = 'itineraries') = (search_text IS NOT NULL)),
  CHECK (kind <> 'itineraries' OR coalesce(
    json_type(payload, '$.date') = 'text' AND itinerary_date = date(itinerary_date), 0
  )),
  CHECK (kind <> 'worship' OR coalesce(
    json_type(payload, '$.lunarDay') = 'integer' AND json_extract(payload, '$.lunarDay') BETWEEN 1 AND 30, 0
  )),
  CHECK (kind <> 'events' OR coalesce(
    json_type(payload, '$.startDate') = 'text' AND
    event_start = strftime('%Y-%m-%dT%H:%M:%fZ', event_start) AND
    event_overlap_end = strftime('%Y-%m-%dT%H:%M:%fZ', event_overlap_end) AND
    event_start <= event_overlap_end, 0
  ))
) STRICT;

CREATE INDEX portal_records_creator ON portal_records (created_by);
CREATE INDEX portal_members_order ON portal_records (kind, member_position, id) WHERE kind = 'members';
CREATE INDEX portal_active_members_order ON portal_records (kind, active, member_position, id) WHERE kind = 'members' AND active = 1;
CREATE INDEX portal_itineraries_cursor ON portal_records (kind, itinerary_date DESC, id DESC) WHERE kind = 'itineraries';
CREATE INDEX portal_worship_order ON portal_records (kind, created_at, id) WHERE kind = 'worship';
CREATE INDEX portal_events_overlap ON portal_records (kind, event_overlap_end, event_start) WHERE kind = 'events';
CREATE INDEX portal_events_repeat ON portal_records (kind, repeat_until, repeat_from) WHERE kind = 'events' AND repeat_until IS NOT NULL;
