begin;

create extension if not exists pgcrypto with schema extensions;
create table portal_private.storage_upload_key (
  singleton boolean primary key default true check (singleton),
  secret bytea not null check (octet_length(secret) = 32)
);
revoke all on portal_private.storage_upload_key from public, anon, authenticated;

-- 只有入口網伺服器驗圖後才會送出短效憑證；正常家人 JWT 不足以直傳物件。
create function portal_private.valid_upload_proof(object_name text) returns boolean
language plpgsql security definer set search_path = ''
as $$
declare
  parts text[] := regexp_match(
    coalesce(nullif(current_setting('request.headers', true), ''), '{}')::jsonb->>'x-portal-upload',
    '^([0-9]{10})[.]([0-9a-f]{64})$'
  );
  issued_until bigint;
  now_seconds bigint := floor(extract(epoch from statement_timestamp()));
  signing_key bytea;
  expected bytea;
begin
  if parts is null or auth.uid() is null then return false; end if;
  issued_until := parts[1]::bigint;
  if issued_until < now_seconds or issued_until > now_seconds + 90 then return false; end if;
  select k.secret into signing_key from portal_private.storage_upload_key as k where k.singleton;
  if signing_key is null then return false; end if;
  expected := extensions.hmac(convert_to(auth.uid()::text || '|' || object_name || '|' || parts[1], 'UTF8'), signing_key, 'sha256');
  return extensions.hmac(decode(parts[2], 'hex'), signing_key, 'sha256') = extensions.hmac(expected, signing_key, 'sha256');
end;
$$;
revoke all on function portal_private.valid_upload_proof(text) from public, anon, authenticated;
grant execute on function portal_private.valid_upload_proof(text) to authenticated;

update storage.buckets
set file_size_limit = 1048576, allowed_mime_types = array['image/webp']
where id = 'family-photos' and public = false;

-- 檔名只能是已存在紀錄下的固定槽位，無法建立任意資料夾或副檔名。
create function portal_private.valid_photo_key(object_name text) returns boolean
language sql immutable set search_path = ''
as $$
  select object_name ~ '^(members|itineraries|worship)/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/[1-5][.]webp$'
    and (split_part(object_name, '/', 1) <> 'members' or split_part(object_name, '/', 3) = '1.webp');
$$;
revoke all on function portal_private.valid_photo_key(text) from public, anon, authenticated;
grant execute on function portal_private.valid_photo_key(text) to authenticated;

create function portal_private.photo_owner_matches(object_name text, lock_owner boolean) returns boolean
language plpgsql security definer set search_path = ''
as $$
declare
  owner_kind text := split_part(object_name, '/', 1);
  owner_id uuid;
  caller_role text := portal_private.current_role();
begin
  if caller_role is null or (lock_owner and caller_role <> 'Admin') then return false; end if;
  if not portal_private.valid_photo_key(object_name) then return false; end if;
  owner_id := split_part(object_name, '/', 2)::uuid;
  if owner_kind = 'worship' then
    if lock_owner then
      perform 1 from public.worship_rules where id = owner_id for key share;
    else
      perform 1 from public.worship_rules where id = owner_id;
    end if;
  else
    if lock_owner then
      perform 1 from public.family_records as f where f.id = owner_id and f.kind = owner_kind for key share;
    else
      perform 1 from public.family_records as f where f.id = owner_id and f.kind = owner_kind
        and (caller_role = 'Admin' or owner_kind <> 'members' or f.details->'isActive' = 'true'::jsonb);
    end if;
  end if;
  return found;
end;
$$;
revoke all on function portal_private.photo_owner_matches(text, boolean) from public, anon, authenticated;
grant execute on function portal_private.photo_owner_matches(text, boolean) to authenticated;

drop policy family_photos_read on storage.objects;
create policy family_photos_read on storage.objects for select to authenticated
  using (bucket_id = 'family-photos' and (
    ((select storage.allow_any_operation(array['object.list', 'object.get_authenticated']))
      and portal_private.photo_owner_matches(name, false))
    or ((select storage.allow_any_operation(array['object.delete_many'])) and (select portal_private.current_role()) = 'Admin'
      and portal_private.valid_photo_key(name))
  ));
create policy family_photos_insert on storage.objects for insert to authenticated
  with check (bucket_id = 'family-photos' and (select storage.allow_any_operation(array['object.upload']))
    and portal_private.valid_upload_proof(name)
    and portal_private.photo_owner_matches(name, true));
create policy family_photos_delete on storage.objects for delete to authenticated
  using (bucket_id = 'family-photos' and (select portal_private.current_role()) = 'Admin'
    and (select storage.allow_any_operation(array['object.delete_many'])) and portal_private.valid_photo_key(name));

-- 透過 Storage API 先刪實體照片；禁止僅刪資料庫紀錄而留下實體物件。
create function portal_private.require_no_photos() returns trigger
language plpgsql security definer set search_path = ''
as $$
declare prefix text := (case when tg_table_name = 'worship_rules' then 'worship' else to_jsonb(old)->>'kind' end)
  || '/' || old.id::text || '/';
begin
  if exists (select 1 from storage.objects where bucket_id = 'family-photos' and name like prefix || '%') then
    raise foreign_key_violation using message = 'photos must be removed first', constraint = 'portal_photos_must_be_removed';
  end if;
  return old;
end;
$$;
revoke all on function portal_private.require_no_photos() from public, anon, authenticated;
create trigger require_no_family_record_photos before delete on public.family_records
for each row execute function portal_private.require_no_photos();
create trigger require_no_worship_photos before delete on public.worship_rules
for each row execute function portal_private.require_no_photos();

commit;
