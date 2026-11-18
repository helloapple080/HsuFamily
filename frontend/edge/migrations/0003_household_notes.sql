-- 家用備忘從空白開始；不修改原有資料或資料表。
CREATE TABLE portal_household_notes (
  id TEXT NOT NULL PRIMARY KEY CHECK (length(id) = 36),
  category TEXT NOT NULL CHECK (category IN ('water', 'store', 'equipment', 'other')),
  title TEXT NOT NULL CHECK (length(title) BETWEEN 1 AND 100 AND instr(title, char(0)) = 0),
  location TEXT NOT NULL DEFAULT '' CHECK (length(location) <= 120 AND instr(location, char(0)) = 0),
  details TEXT NOT NULL CHECK (length(details) BETWEEN 1 AND 2000 AND instr(details, char(0)) = 0),
  version INTEGER NOT NULL DEFAULT 1 CHECK (version BETWEEN 1 AND 9007199254740991),
  created_by TEXT NOT NULL REFERENCES portal_access (id) ON DELETE RESTRICT,
  updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
) STRICT;

CREATE INDEX portal_household_recent ON portal_household_notes (updated_at DESC, id DESC);
CREATE INDEX portal_household_creator ON portal_household_notes (created_by);
