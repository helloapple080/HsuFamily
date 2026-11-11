begin;

create schema portal_private;
revoke all on schema portal_private from public, anon, authenticated;
grant usage on schema portal_private to authenticated;

create table public.portal_access (
  user_id uuid primary key references auth.users(id) on delete cascade,
  role text not null check (role in ('Admin', 'User'))
);
alter table public.portal_access enable row level security;
revoke all on public.portal_access from public, anon, authenticated;
grant select on public.portal_access to authenticated;
create policy portal_access_read_self on public.portal_access for select to authenticated
  using (user_id = (select auth.uid()));

-- 只讀取呼叫者自身的角色；不接受外部傳入身分。
create function portal_private.current_role() returns text
language sql stable security definer set search_path = ''
as $$
  select a.role from public.portal_access as a where a.user_id = (select auth.uid());
$$;
revoke all on function portal_private.current_role() from public, anon, authenticated;
grant execute on function portal_private.current_role() to authenticated;

create table public.worship_rules (
  id uuid primary key default gen_random_uuid(),
  title text not null check (char_length(btrim(title)) between 1 and 100),
  -- 空月份表示每個農曆月，非空表示每年指定月。
  lunar_month smallint check (lunar_month between 1 and 12),
  lunar_day smallint not null check (lunar_day between 1 and 30),
  leap_month_policy text not null default 'RegularOnly'
    check (leap_month_policy in ('RegularOnly', 'LeapOnly', 'Both')),
  notes text not null default '' check (char_length(notes) <= 2000),
  created_at timestamptz not null default now()
);
alter table public.worship_rules enable row level security;
revoke all on public.worship_rules from public, anon, authenticated;
grant select, delete on public.worship_rules to authenticated;
grant insert (title, lunar_month, lunar_day, leap_month_policy, notes),
      update (title, lunar_month, lunar_day, leap_month_policy, notes)
  on public.worship_rules to authenticated;
create policy worship_rules_read on public.worship_rules for select to authenticated
  using ((select portal_private.current_role()) in ('Admin', 'User'));
create policy worship_rules_insert on public.worship_rules for insert to authenticated
  with check ((select portal_private.current_role()) = 'Admin');
create policy worship_rules_update on public.worship_rules for update to authenticated
  using ((select portal_private.current_role()) = 'Admin')
  with check ((select portal_private.current_role()) = 'Admin');
create policy worship_rules_delete on public.worship_rules for delete to authenticated
  using ((select portal_private.current_role()) = 'Admin');

insert into storage.buckets (id, name, public) values ('family-photos', 'family-photos', false);
create policy family_photos_read on storage.objects for select to authenticated
  using (bucket_id = 'family-photos' and (select portal_private.current_role()) in ('Admin', 'User'));
-- 不授予上傳權限。先完成圖片內容驗證與附件關聯，再啟用管理員上傳。

commit;
