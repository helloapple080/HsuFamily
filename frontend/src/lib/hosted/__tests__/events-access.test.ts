// @vitest-environment node
import { readFileSync } from 'node:fs'
import { PGlite } from '@electric-sql/pglite'
import { afterAll, beforeAll, expect, it } from 'vitest'

const db = new PGlite()
const admin = '10000000-0000-4000-8000-000000000001'
const family = '10000000-0000-4000-8000-000000000002'
const outsider = '10000000-0000-4000-8000-000000000003'
const insert = `insert into public.calendar_events (starts_at, details) values
  ('2026-11-12T00:00:00Z', '{"title":"祭祖","eventType":"Worship","isAllDay":true,"isLunarDate":false,"relatedMemberIds":[],"tags":[]}')
  returning id, created_by`

beforeAll(async () => {
  await db.exec(`
    create role anon;
    create role authenticated;
    create schema auth;
    create table auth.users (id uuid primary key);
    create function auth.uid() returns uuid language sql stable as
      $$ select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid $$;
    grant usage on schema auth to anon, authenticated;
    insert into auth.users values ('${admin}'), ('${family}'), ('${outsider}');
    create schema storage;
    create table storage.buckets (id text primary key, name text, public boolean);
    create table storage.objects (bucket_id text, name text);
    alter table storage.objects enable row level security;
  `)
  for (const name of ['202611110001_portal_foundation', '202611120003_calendar_events']) {
    await db.exec(readFileSync(new URL(`../../../../../supabase/migrations/${name}.sql`, import.meta.url), 'utf8'))
  }
  await db.query("insert into public.portal_access (user_id, role) values ($1, 'Admin'), ($2, 'User')", [admin, family])
})

afterAll(() => db.close())

async function as<T>(role: 'anon' | 'authenticated', uid: string, sql: string, args: string[] = []) {
  return db.transaction(async (tx) => {
    await tx.exec(`set local role ${role}`)
    await tx.query("select set_config('request.jwt.claim.sub', $1, true)", [uid])
    return (await tx.query<T>(sql, args)).rows
  })
}

it('由 RLS 保護事件，只有管理員可新增、修改及刪除，稽核身分不可覆寫', async () => {
  const [created] = await as<{ id: string; created_by: string }>('authenticated', admin, insert)
  expect(created.created_by).toBe(admin)
  await expect(as('anon', '', 'select id from public.calendar_events')).rejects.toThrow(/permission denied/)
  await expect(as('authenticated', outsider, 'select id from public.calendar_events')).resolves.toEqual([])
  await expect(as('authenticated', family, 'select id from public.calendar_events')).resolves.toEqual([{ id: created.id }])
  await expect(as('authenticated', family, insert)).rejects.toThrow(/row-level security/)
  await expect(as('authenticated', family, 'delete from public.calendar_events returning id')).resolves.toEqual([])
  await expect(as('authenticated', family, 'update public.calendar_events set ends_at = starts_at returning id')).resolves.toEqual([])
  await expect(as('authenticated', admin, 'update public.calendar_events set created_by = $1', [family]))
    .rejects.toThrow(/permission denied/)
  await expect(as('authenticated', admin, 'update public.calendar_events set ends_at = starts_at returning id'))
    .resolves.toEqual([{ id: created.id }])
  await expect(as('authenticated', admin, 'delete from public.calendar_events where id = $1 returning id', [created.id]))
    .resolves.toEqual([{ id: created.id }])
})
