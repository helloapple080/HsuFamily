// @vitest-environment node
import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { exportJWK, generateKeyPair, SignJWT, type JWK } from 'jose'
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { getPlatformProxy, unstable_splitSqlQuery, type PlatformProxy } from 'wrangler'
import { z } from 'zod'
import { householdLimit } from '@/lib/hosted/household-contract'
import worker from '../index'

const origin = 'https://family.example.test'
const issuer = 'https://test-portal.cloudflareaccess.com'
const audience = 'a'.repeat(64)
const kid = 'synthetic-test-key'
const recordId = '9e43deef-7580-40d1-aa15-e9561971b576'
const worship = { title: '驗收假資料', lunarMonth: 8, lunarDay: 15, leapMonthPolicy: 'RegularOnly', notes: '' }
const recordsPath = '/api/hosted/worship'
const sessionPath = '/api/hosted/auth/me'
const householdPath = '/api/hosted/household'
const note = { category: 'water', title: '演練開關', location: '三樓', details: '只供自動測試' }

let platform: PlatformProxy<{ DB: D1Database }> | undefined
let publicJwk: JWK
const identities = {
  admin: { id: '4b2d883e-6a6e-48fc-a4da-7119fb2bfdba', email: 'admin@example.test', role: 'Admin' },
  reader: { id: '75a57db9-e68a-4682-b9fb-1b735c5890a7', email: 'reader@example.test', role: 'User' },
  revoked: { id: '2f47a7cd-eca9-492b-9cad-6e3b3625fe58', email: 'revoked@example.test', role: 'User' },
  unlisted: { email: 'unlisted@example.test' },
} as const
type Identity = keyof typeof identities
const tokens = new Map<Identity, string>()

function database() {
  if (!platform) throw new Error('本機 D1 尚未啟動')
  return platform.env.DB
}

beforeAll(async () => {
  platform = await getPlatformProxy<{ DB: D1Database }>({
    configPath: fileURLToPath(new URL('../../wrangler.jsonc', import.meta.url)),
    persist: false,
    remoteBindings: false,
    envFiles: [],
  })
  const db = database()
  for (const migration of ['0001_portal.sql', '0002_photos.sql', '0003_household_notes.sql']) {
    const sql = await readFile(new URL(`../migrations/${migration}`, import.meta.url), 'utf8')
    await db.batch(unstable_splitSqlQuery(sql).map((statement) => db.prepare(statement)))
  }
  for (const { id, email, role } of [identities.admin, identities.reader, identities.revoked]) {
    await db.prepare('INSERT INTO portal_access (id, email, role) VALUES (?, ?, ?)').bind(id, email, role).run()
  }
  await db.prepare("INSERT INTO portal_records (id, kind, payload, created_by) VALUES (?, 'worship', ?, ?)")
    .bind(recordId, JSON.stringify(worship), identities.admin.id).run()

  const { publicKey, privateKey } = await generateKeyPair('RS256')
  publicJwk = { ...await exportJWK(publicKey), kid, alg: 'RS256' }
  for (const identity of ['admin', 'reader', 'revoked', 'unlisted'] as const) {
    const token = await new SignJWT({ email: identities[identity].email, type: 'app' })
      .setProtectedHeader({ alg: 'RS256', kid }).setIssuer(issuer).setAudience(audience)
      .setSubject(`test-subject-${identity}`).setIssuedAt().setExpirationTime('10m').sign(privateKey)
    tokens.set(identity, token)
  }
}, 15000)

beforeEach(() => {
  // 只替換外部 JWKS 回應；正式入口、簽章驗證與 D1 查詢都照原程式執行。
  const fetchOriginal = globalThis.fetch
  vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) =>
    String(input) === `${issuer}/cdn-cgi/access/certs`
      ? Promise.resolve(Response.json({ keys: [publicJwk] }))
      : fetchOriginal(input, init))
})

afterAll(async () => { await platform?.dispose() })

function call(identity: Identity, path: string, method = 'GET', from = origin, input: object = { ...worship, title: '不該寫入的假資料' }, match: string | null = '"1"') {
  const token = tokens.get(identity)
  if (!token) throw new Error('未建立合成測試權杖')
  const headers = new Headers({ 'Cf-Access-Jwt-Assertion': token, Origin: from })
  if (match) headers.set('If-Match', match)
  const body = method === 'POST' || method === 'PUT' ? JSON.stringify(input) : undefined
  if (body) headers.set('Content-Type', 'application/json')
  return worker.fetch(new Request(`${origin}${path}`, { method, headers, body }), {
    DB: database(), ACCESS_ISSUER: issuer, ACCESS_AUDIENCE: audience, PORTAL_ORIGIN: origin,
  })
}

async function expectOriginalRecord(identity: Identity) {
  const response = await call(identity, recordsPath)
  expect(response.status).toBe(200)
  expect(response.headers.get('Cache-Control')).toBe('private, no-store')
  expect(await response.json()).toMatchObject({ data: [{ ...worship, id: recordId, version: 1 }] })
}

describe('Cloudflare 私人 API 權限整合演練', () => {
  it('已核准的唯讀家人可查詢，但新增、修改與刪除均無效', async () => {
    const session = await call('reader', sessionPath)
    expect(session.status).toBe(200)
    expect(await session.json()).toMatchObject({ data: { role: 'User' } })
    for (const [method, path] of [['POST', recordsPath], ['PUT', `${recordsPath}/${recordId}`], ['DELETE', `${recordsPath}/${recordId}`]]) {
      expect((await call('reader', path, method)).status, method).toBe(403)
    }
    await expectOriginalRecord('reader')
  })

  it('具有有效簽章的未核准帳號也讀不到資料', async () => {
    expect((await call('unlisted', recordsPath)).status).toBe(403)
  })

  it('停用帳號後，同一張未過期權杖立即失去資料讀取資格', async () => {
    await expectOriginalRecord('revoked')
    const update = await database().prepare('UPDATE portal_access SET active = 0 WHERE id = ?').bind(identities.revoked.id).run()
    expect(update.meta.changes).toBe(1)
    for (const path of [sessionPath, recordsPath]) {
      const denied = await call('revoked', path)
      expect(denied.status).toBe(403)
      expect(denied.headers.get('Cache-Control')).toBe('private, no-store')
      expect(await denied.json()).toMatchObject({ data: null })
    }
  })

  it('即使管理員已登入，來自其他網站的寫入也會被擋下', async () => {
    const session = await call('admin', sessionPath)
    expect(session.status).toBe(200)
    expect(await session.json()).toMatchObject({ data: { role: 'Admin' } })
    expect((await call('admin', `${recordsPath}/${recordId}`, 'PUT', 'https://other.example.test')).status).toBe(403)
    await expectOriginalRecord('admin')
  })

  it('家用備忘只供核准家人讀取，管理員寫入有驗證及版本保護', async () => {
    const empty = await call('reader', householdPath)
    expect(empty.status).toBe(200)
    expect(empty.headers.get('Cache-Control')).toBe('private, no-store')
    expect(await empty.json()).toMatchObject({ data: [] })
    expect((await worker.fetch(new Request(`${origin}${householdPath}`), {
      DB: database(), ACCESS_ISSUER: issuer, ACCESS_AUDIENCE: audience, PORTAL_ORIGIN: origin,
    })).status).toBe(401)
    expect((await call('unlisted', householdPath)).status).toBe(403)
    expect((await call('admin', `${householdPath}?search=water`)).status).toBe(400)
    expect((await call('admin', householdPath, 'POST', 'https://other.example.test', note)).status).toBe(403)
    for (const invalid of [{ ...note, category: 'secret' }, { ...note, version: 3 }, { ...note, details: '字'.repeat(2001) }]) {
      expect((await call('admin', householdPath, 'POST', origin, invalid)).status).toBe(400)
    }

    const created = await call('admin', householdPath, 'POST', origin, note)
    expect(created.status).toBe(201)
    const { data: { id } } = z.object({ data: z.object({ id: z.uuid(), version: z.literal(1) }) }).parse(await created.json())
    const path = `${householdPath}/${id}`
    for (const [method, url] of [['POST', householdPath], ['PUT', path], ['DELETE', path]]) {
      expect((await call('reader', url, method, origin, note)).status, method).toBe(403)
    }
    expect(await (await call('reader', householdPath)).json()).toMatchObject({ data: [{ id, ...note, version: 1 }] })
    expect((await call('admin', path, 'PUT', origin, note, null)).status).toBe(428)
    const updated = await call('admin', path, 'PUT', origin, { ...note, location: '一樓' })
    expect(updated.status).toBe(200)
    expect(await updated.json()).toMatchObject({ data: { id, location: '一樓', version: 2 } })
    expect((await call('admin', path, 'PUT', origin, note)).status).toBe(409)
    const stale = await call('admin', path, 'DELETE')
    expect(stale.status).toBe(409)
    expect(await stale.json()).toMatchObject({ errors: ['VERSION_CONFLICT'] })
    expect((await call('admin', path, 'DELETE', origin, note, '"2"')).status).toBe(200)
    expect(await (await call('admin', householdPath)).json()).toMatchObject({ data: [] })
    expect((await call('admin', path, 'DELETE', origin, note, '"2"')).status).toBe(404)
  })

  it('備忘達到上限後拒絕再新增，原資料仍可查詢', async () => {
    const db = database()
    const seed = db.prepare(`INSERT INTO portal_household_notes (id, category, title, location, details, created_by)
      WITH RECURSIVE numbered(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM numbered WHERE n < ?)
      SELECT printf('70000000-0000-4000-8000-%012d', n), ?, ?, ?, ?, ? FROM numbered`)
    await seed.bind(householdLimit, note.category, note.title, note.location, note.details, identities.admin.id).run()
    try {
      const full = await call('admin', householdPath, 'POST', origin, note)
      expect(full.status).toBe(409)
      expect(await full.json()).toMatchObject({ errors: ['HOUSEHOLD_LIMIT'] })
      const { data } = z.object({ data: z.array(z.unknown()) }).parse(await (await call('reader', householdPath)).json())
      expect(data).toHaveLength(householdLimit)
    } finally {
      await db.prepare("DELETE FROM portal_household_notes WHERE id LIKE '70000000-0000-4000-8000-%'").run()
    }
  })
})
