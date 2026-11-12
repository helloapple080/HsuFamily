begin;

create table public.family_records (
  id uuid primary key default gen_random_uuid(),
  kind text not null check (kind in ('members', 'itineraries')),
  civil_date date not null check (civil_date between date '0001-01-01' and date '9999-12-31'),
  label text not null check (char_length(btrim(label)) between 1 and 200),
  position integer not null default 0,
  search_text text not null check (char_length(search_text) <= 8000),
  details jsonb not null check (jsonb_typeof(details) = 'object' and octet_length(details::text) <= 131072),
  created_by uuid not null default auth.uid(),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index family_records_member_order on public.family_records (position, id) where kind = 'members';
create index family_records_itinerary_order on public.family_records (civil_date desc, id desc) where kind = 'itineraries';

create function portal_private.touch_family_record() returns trigger
language plpgsql set search_path = ''
as $$ begin new.updated_at = now(); return new; end; $$;
revoke all on function portal_private.touch_family_record() from public, anon, authenticated;
create trigger touch_family_record before update on public.family_records
for each row execute function portal_private.touch_family_record();

alter table public.family_records enable row level security;
revoke all on public.family_records from public, anon, authenticated;
grant select, delete on public.family_records to authenticated;
grant insert (kind, civil_date, label, position, search_text, details),
      update (civil_date, label, position, search_text, details) on public.family_records to authenticated;
create policy family_records_read on public.family_records for select to authenticated
  using ((select portal_private.current_role()) = 'Admin'
    or ((select portal_private.current_role()) = 'User' and (kind <> 'members' or details->'isActive' = 'true'::jsonb)));
create policy family_records_add on public.family_records for insert to authenticated
  with check ((select portal_private.current_role()) = 'Admin');
create policy family_records_edit on public.family_records for update to authenticated
  using ((select portal_private.current_role()) = 'Admin')
  with check ((select portal_private.current_role()) = 'Admin');
create policy family_records_remove on public.family_records for delete to authenticated
  using ((select portal_private.current_role()) = 'Admin');

commit;
