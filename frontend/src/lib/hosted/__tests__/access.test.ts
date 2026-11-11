// @vitest-environment node
import { readFileSync } from 'node:fs'
import { PGlite } from '@electric-sql/pglite'
import { afterAll, beforeAll, describe, expect, it } from 'vitest'

const db = new PGlite()
const admin = '10000000-0000-4000-8000-000000000001'
const member = '10000000-0000-4000-8000-000000000002'
const outsider = '10000000-0000-4000-8000-000000000003'
const migration = new URL('../../../../../supabase/migrations/202611110001_portal_foundation.sql', import.meta.url)
const insertRule = "insert into public.worship_rules(title, lunar_month, lunar_day) values ('中秋祭拜', 8, 15)"

beforeAll(async () => {
  // 模擬 Supabase 提供的角色與 JWT 身分函式，資料表及政策使用正式 SQL。
  await db.exec(`
    create role anon;
    create role authenticated;
    create schema auth;
    create table auth.users (id uuid primary key);
    create function auth.uid() returns uuid language sql stable as
      $$ select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid $$;
    grant usage on schema auth to anon, authenticated;
    insert into auth.users values ('${admin}'), ('${member}'), ('${outsider}');
    create schema storage;
    create table storage.buckets (id text primary key, name text, public boolean);
    create table storage.objects (bucket_id text, name text);
    alter table storage.objects enable row level security;
    grant usage on schema storage to anon, authenticated;
    grant select, insert on storage.objects to anon, authenticated;
  `)
  await db.exec(readFileSync(migration, 'utf8'))
  await db.exec(`
    insert into public.portal_access values ('${admin}', 'Admin'), ('${member}', 'User');
    ${insertRule};
    insert into storage.objects values ('family-photos', 'private.webp');
  `)
})

afterAll(async () => { await db.close() })

async function as(role: 'anon' | 'authenticated', uid: string, sql: string) {
  return db.transaction(async (tx) => {
    await tx.exec(`set local role ${role}`)
    await tx.query("select set_config('request.jwt.claim.sub', $1, true)", [uid])
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
      .resolves.toEqual([{ name: 'private.webp' }])
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

  it('私人桶不開放直接上傳；撤權立即阻止讀取', async () => {
    await expect(db.query("select public from storage.buckets where id = 'family-photos'"))
      .resolves.toMatchObject({ rows: [{ public: false }] })
    await expect(as('authenticated', admin, "insert into storage.objects values ('family-photos', 'unsafe.webp')"))
      .rejects.toThrow(/row-level security/)
    await db.query('delete from public.portal_access where user_id = $1', [member])
    await expect(as('authenticated', member, 'select * from public.worship_rules')).resolves.toEqual([])
    await expect(as('authenticated', member, 'select * from storage.objects')).resolves.toEqual([])
  })
})
