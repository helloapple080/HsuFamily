begin;

create table public.calendar_events (
  id uuid primary key default gen_random_uuid(),
  starts_at timestamptz not null,
  ends_at timestamptz,
  overlap_end timestamptz generated always as (coalesce(ends_at, starts_at)) stored,
  details jsonb not null,
  repeat_kind text generated always as (details -> 'recurrenceRule' ->> 'type') stored,
  created_by uuid not null default auth.uid(),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint calendar_event_dates check (
    starts_at >= timestamptz '1901-01-01 00:00:00+00'
    and starts_at < timestamptz '2101-01-01 00:00:00+00'
    and (ends_at is null or (ends_at >= starts_at and ends_at < timestamptz '2101-01-01 00:00:00+00'))
  ),
  constraint calendar_event_details check (
    jsonb_typeof(details) = 'object'
    and octet_length(details::text) <= 131072
    and jsonb_typeof(details->'title') is not distinct from 'string'
    and char_length(details->>'title') between 1 and 200
    and (details->>'eventType' in ('General', 'Birthday', 'Festival', 'Worship', 'Anniversary', 'Meeting', 'Other')) is true
    and jsonb_typeof(details->'isAllDay') is not distinct from 'boolean'
    and jsonb_typeof(details->'isLunarDate') is not distinct from 'boolean'
    and jsonb_typeof(details->'tags') is not distinct from 'array'
    and jsonb_typeof(details->'relatedMemberIds') is not distinct from 'array'
    and (repeat_kind is null or repeat_kind in ('Yearly', 'Monthly', 'LunarYearly', 'LunarMonthly'))
    and (repeat_kind is null or ends_at is null or ends_at - starts_at <= interval '366 days')
  )
);

create index calendar_events_start on public.calendar_events (starts_at, id);
create index calendar_events_single_overlap on public.calendar_events (overlap_end, starts_at) where repeat_kind is null;
create index calendar_events_recurring_start on public.calendar_events (starts_at, id) where repeat_kind is not null;

create function portal_private.touch_calendar_event() returns trigger
language plpgsql set search_path = ''
as $$ begin new.updated_at = now(); return new; end; $$;
revoke all on function portal_private.touch_calendar_event() from public, anon, authenticated;
create trigger touch_calendar_event before update on public.calendar_events
for each row execute function portal_private.touch_calendar_event();

alter table public.calendar_events enable row level security;
revoke all on public.calendar_events from public, anon, authenticated;
grant select, delete on public.calendar_events to authenticated;
grant insert (starts_at, ends_at, details), update (starts_at, ends_at, details)
  on public.calendar_events to authenticated;
create policy calendar_events_read on public.calendar_events for select to authenticated
  using ((select portal_private.current_role()) in ('Admin', 'User'));
create policy calendar_events_add on public.calendar_events for insert to authenticated
  with check ((select portal_private.current_role()) = 'Admin');
create policy calendar_events_edit on public.calendar_events for update to authenticated
  using ((select portal_private.current_role()) = 'Admin')
  with check ((select portal_private.current_role()) = 'Admin');
create policy calendar_events_remove on public.calendar_events for delete to authenticated
  using ((select portal_private.current_role()) = 'Admin');

commit;
