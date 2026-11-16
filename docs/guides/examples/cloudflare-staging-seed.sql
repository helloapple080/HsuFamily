-- 僅供「hsu-family-staging」還原演練，禁止套用到正式資料庫。
-- example.com 是示例網域。此帳號停用，不可用來登入，不會發送驗證信。
INSERT INTO portal_access (id, email, active, role)
VALUES ('d6f8abfb-1b29-4d12-9ed6-39162d2151e9', 'restore-check@example.com', 0, 'User')
ON CONFLICT (id) DO NOTHING;

INSERT INTO portal_records (id, kind, payload, created_by)
VALUES ('6f016e67-e2c7-4b42-9c6a-be348ed1f387', 'worship',
  '{"title":"假資料：還原演練","lunarMonth":8,"lunarDay":15,"leapMonthPolicy":"RegularOnly","notes":""}',
  'd6f8abfb-1b29-4d12-9ed6-39162d2151e9')
ON CONFLICT (id) DO UPDATE SET payload = excluded.payload
WHERE portal_records.kind = excluded.kind AND portal_records.created_by = excluded.created_by
  AND portal_records.payload <> excluded.payload;
