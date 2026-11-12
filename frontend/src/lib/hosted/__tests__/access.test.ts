// @vitest-environment node
import { readFileSync } from 'node:fs'
import { createHmac } from 'node:crypto'
import { PGlite } from '@electric-sql/pglite'
import { pgcrypto } from '@electric-sql/pglite/contrib/pgcrypto'
import { afterAll, beforeAll, describe, expect, it } from 'vitest'

const db = new PGlite({ extensions: { pgcrypto } })
const secret = '41'.repeat(32)
const admin = '10000000-0000-4000-8000-000000000001'
const member = '10000000-0000-4000-8000-000000000002'
const outsider = '10000000-0000-4000-8000-000000000003'
const migration = new URL('../../../../../supabase/migrations/202611110001_portal_foundation.sql', import.meta.url)
const familyMigration = new URL('../../../../../supabase/migrations/202611120001_family_records.sql', import.meta.url)
const photoMigration = new URL('../../../../../supabase/migrations/202611120002_private_photos.sql', import.meta.url)
const photoOwner = '20000000-0000-4000-8000-000000000001'
const photoKey = `worship/${photoOwner}/1.webp`
const insertRule = "insert into public.worship_rules(title, lunar_month, lunar_day) values ('中秋祭拜', 8, 15)"

beforeAll(async () => {
  // 模擬 Supabase 提供的角色與 JWT 身分函式，資料表及政策使用正式 SQL。
  await db.exec(`
    create role anon;
    create role authenticated;
    create schema extensions;
    create schema auth;
    create table auth.users (id uuid primary key);
    create function auth.uid() returns uuid language sql stable as
      $$ select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid $$;
    grant usage on schema auth to anon, authenticated;
    insert into auth.users values ('${admin}'), ('${member}'), ('${outsider}');
    create schema storage;
    create table storage.buckets (id text primary key, name text, public boolean, file_size_limit bigint, allowed_mime_types text[]);
    create table storage.objects (bucket_id text, name text);
    create function storage.allow_any_operation(operations text[]) returns boolean language sql stable
      as $$ select coalesce(current_setting('storage.operation', true) = any(operations), false) $$;
    alter table storage.objects enable row level security;
    grant usage on schema storage to anon, authenticated;
    grant select, insert, update, delete on storage.objects to anon, authenticated;
  `)
  await db.exec(readFileSync(migration, 'utf8'))
  await db.exec(readFileSync(familyMigration, 'utf8'))
  await db.exec(readFileSync(photoMigration, 'utf8'))
  await db.exec(`insert into portal_private.storage_upload_key (secret) values (decode('${secret}', 'hex'))`)
  await db.exec(`
    insert into public.portal_access values ('${admin}', 'Admin'), ('${member}', 'User');
    insert into public.worship_rules(id, title, lunar_month, lunar_day) values ('${photoOwner}', '中秋祭拜', 8, 15);
    insert into storage.objects values ('family-photos', '${photoKey}');
  `)
})

afterAll(async () => { await db.close() })

function photoProof(uid: string, key: string, offset = 45) {
  const expires = Math.floor(Date.now() / 1000) + offset
  return expires + '.' + createHmac('sha256', Buffer.from(secret, 'hex')).update(`${uid}|${key}|${expires}`).digest('hex')
}

async function as(role: 'anon' | 'authenticated', uid: string, sql: string, operation = 'object.list', proof = '') {
  return db.transaction(async (tx) => {
    await tx.exec(`set local role ${role}`)
    await tx.query("select set_config('request.jwt.claim.sub', $1, true)", [uid])
    await tx.query("select set_config('storage.operation', $1, true)", [operation])
    await tx.query("select set_config('request.headers', $1, true)", [JSON.stringify({ 'x-portal-upload': proof })])
    return (await tx.query(sql)).rows
  })
}

describe('Supabase 家族資料權限', () => {
  it('匿名與未核准帳號無法讀取農曆規則或照片', async () => {
    await expect(as('anon', '', 'select * from public.worship_rules')).rejects.toThrow(/permission denied/)
    await expect(as('authenticated', outsider, 'select * from public.worship_rules')).resolves.toEqual([])
    await expect(as('authenticated', outsider, 'select * from storage.objects')).resolves.toEqual([])
  })

  it('核准家人可讀、不可寫或將自己改成管理員', async () => {
    await expect(as('authenticated', member, 'select title from public.worship_rules'))
      .resolves.toEqual([{ title: '中秋祭拜' }])
    await expect(as('authenticated', member, 'select name from storage.objects'))
      .resolves.toEqual([{ name: photoKey }])
    for (const operation of ['object.sign', 'object.sign_many', 'object.sign_upload_url', '']) {
      await expect(as('authenticated', member, 'select name from storage.objects', operation)).resolves.toEqual([])
      await expect(as('authenticated', admin, 'select name from storage.objects', operation)).resolves.toEqual([])
    }
    await expect(as('authenticated', member, insertRule)).rejects.toThrow(/row-level security/)
    await expect(as('authenticated', member, "update public.portal_access set role = 'Admin'"))
      .rejects.toThrow(/permission denied/)
  })

  it('管理員可維護規則，但不允許超出農曆日期範圍', async () => {
    await expect(as('authenticated', admin, "update public.worship_rules set title = '中秋拜拜' returning title"))
      .resolves.toEqual([{ title: '中秋拜拜' }])
    await expect(as('authenticated', admin, 'update public.worship_rules set lunar_day = 31'))
      .rejects.toThrow(/check constraint/)
  })

  it('成員與行程資料的直接資料庫操作也受角色限制', async () => {
    const insert = "insert into public.family_records(kind, civil_date, label, search_text, details) values ('members', '1985-01-01', '測試家人', '測試家人', '{}') returning id, label"
    const activeInsert = insert.replace("'{}'", "'{\"isActive\":true}'")
    await expect(as('anon', '', 'select * from public.family_records')).rejects.toThrow(/permission denied/)
    await expect(as('authenticated', member, insert)).rejects.toThrow(/row-level security/)
    const [row] = await as('authenticated', admin, activeInsert) as { id: string; label: string }[]
    expect(row.label).toBe('測試家人')
    await expect(as('authenticated', member, 'select label from public.family_records')).resolves.toEqual([{ label: '測試家人' }])
    await expect(as('authenticated', outsider, 'select label from public.family_records')).resolves.toEqual([])
    await expect(as('authenticated', member, 'delete from public.family_records returning label')).resolves.toEqual([])
    await expect(as('authenticated', admin, "update public.family_records set kind = 'itineraries'"))
      .rejects.toThrow(/permission denied/)
    await expect(as('authenticated', admin, `insert into storage.objects values ('family-photos', 'members/${row.id}/1.webp')`, 'object.upload', photoProof(admin, `members/${row.id}/1.webp`)))
      .resolves.toEqual([])
    await as('authenticated', admin, `update public.family_records set details = '{"isActive": false}' where id = '${row.id}'`)
    await expect(as('authenticated', member, 'select id from public.family_records')).resolves.toEqual([])
    await expect(as('authenticated', member, `select name from storage.objects where name = 'members/${row.id}/1.webp'`, 'object.get_authenticated'))
      .resolves.toEqual([])
    await expect(as('authenticated', admin, `select name from storage.objects where name = 'members/${row.id}/1.webp'`, 'object.get_authenticated'))
      .resolves.toEqual([{ name: `members/${row.id}/1.webp` }])
    await expect(as('authenticated', admin, `insert into storage.objects values ('family-photos', 'members/${row.id}/2.webp')`, 'object.upload'))
      .rejects.toThrow(/row-level security/)
    await expect(as('authenticated', admin, `delete from public.family_records where id = '${row.id}'`))
      .rejects.toThrow(/photos must be removed/)
    await as('authenticated', admin, `delete from storage.objects where name = 'members/${row.id}/1.webp'`, 'object.delete_many')
    await expect(as('authenticated', admin, `delete from public.family_records where id = '${row.id}' returning label`))
      .resolves.toEqual([{ label: '測試家人' }])
  })

  it('私人桶只准管理員對有效的關聯與槽位寫入；撤權立即阻止讀取', async () => {
    await expect(db.query("select public from storage.buckets where id = 'family-photos'"))
      .resolves.toMatchObject({ rows: [{ public: false }] })
    await expect(as('authenticated', admin, "insert into storage.objects values ('family-photos', 'unsafe.webp')", 'object.upload'))
      .rejects.toThrow(/row-level security/)
    const second = `insert into storage.objects values ('family-photos', 'worship/${photoOwner}/2.webp')`
    await expect(as('authenticated', member, second, 'object.upload')).rejects.toThrow(/row-level security/)
    await expect(as('authenticated', admin, second, 'object.sign_upload_url')).rejects.toThrow(/row-level security/)
    await expect(as('authenticated', admin, second, 'object.upload')).rejects.toThrow(/row-level security/)
    await expect(as('authenticated', admin, second, 'object.upload', photoProof(admin, photoKey))).rejects.toThrow(/row-level security/)
    await expect(as('authenticated', admin, second, 'object.upload', photoProof(admin, `worship/${photoOwner}/2.webp`, -10))).rejects.toThrow(/row-level security/)
    await expect(as('authenticated', admin, second, 'object.upload', photoProof(admin, `worship/${photoOwner}/2.webp`))).resolves.toEqual([])
    await expect(as('authenticated', admin, "insert into storage.objects values ('family-photos', 'worship/20000000-0000-4000-8000-000000000002/2.webp')", 'object.upload'))
      .rejects.toThrow(/row-level security/)
    await expect(as('authenticated', admin, `delete from public.worship_rules where id = '${photoOwner}'`))
      .rejects.toThrow(/photos must be removed/)
    await db.query('delete from public.portal_access where user_id = $1', [member])
    await expect(as('authenticated', member, 'select * from public.worship_rules')).resolves.toEqual([])
    await expect(as('authenticated', member, 'select * from storage.objects')).resolves.toEqual([])
  })
})
