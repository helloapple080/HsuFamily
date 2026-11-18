-- 只增加可選日期；既有備忘維持原內容和版本。
ALTER TABLE portal_household_notes ADD COLUMN completed_on TEXT CHECK (
  completed_on IS NULL OR coalesce(date(completed_on, '+0 days') = completed_on
    AND completed_on BETWEEN '1901-01-01' AND '2100-12-31', 0)
);

ALTER TABLE portal_household_notes ADD COLUMN due_on TEXT CHECK (
  (due_on IS NULL OR coalesce(date(due_on, '+0 days') = due_on
    AND due_on BETWEEN '1901-01-01' AND '2100-12-31', 0))
  AND (completed_on IS NULL OR due_on IS NULL OR due_on >= completed_on)
);
