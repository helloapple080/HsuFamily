// @vitest-environment node
import { readFileSync } from 'node:fs'
import { DatabaseSync } from 'node:sqlite'
import sharp from 'sharp'
import { afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { UserInfo } from '@/types/auth'
import { handlePhotos } from '../photos'

const admin = { id: 'ff2d4dd2-2ebc-42bd-856e-628261323bd1', role: 'Admin', username: 'test', email: 'test@example.com', displayName: '測試' } satisfies UserInfo
const member = { ...admin, role: 'User' } satisfies UserInfo
const owner = '6f590021-c16a-4c4e-a70f-43cbcd5648ee'
const path = ['members', owner]
const mib = 1024 * 1024
let jpeg: Uint8Array<ArrayBuffer>
let webp: Uint8Array<ArrayBuffer>
let webpWithExif: Uint8Array<ArrayBuffer>
const databases: DatabaseSync[] = []

afterEach(() => { for (const db of databases.splice(0)) db.close() })

beforeAll(async () => {
  const create = { width: 1500, height: 20, channels: 3 as const, background: '#ff6600' }
  jpeg = new Uint8Array(await sharp({ create }).withExif({ IFD0: { Artist: 'Private Data' } }).jpeg().toBuffer())
  webp = new Uint8Array(await sharp(jpeg).resize(1280).webp().toBuffer())
  webpWithExif = new Uint8Array(await sharp(jpeg).resize(1280).keepExif().webp().toBuffer())
})

function fixture(active = 1) {
  const sqlite = new DatabaseSync(':memory:')
  databases.push(sqlite)
  sqlite.exec(readFileSync(new URL('../migrations/0001_portal.sql', import.meta.url), 'utf8'))
  sqlite.exec(readFileSync(new URL('../migrations/0002_photos.sql', import.meta.url), 'utf8'))
  sqlite.prepare('INSERT INTO portal_access (id, email, role) VALUES (?, ?, ?)').run(admin.id, admin.email, admin.role)
  sqlite.prepare('INSERT INTO portal_records (kind, id, active, payload, created_by) VALUES (?, ?, ?, ?, ?)')
    .run('members', owner, active, JSON.stringify({ isActive: active === 1, sortOrder: 0 }), admin.id)
  const objects = new Map<string, ArrayBuffer>()
  const fault = { write: false, remove: false, afterIndexCommit: false, transform: false }
  let transformed = webp
  let lookupCount = 0
  const db = { prepare: (query: string) => ({ bind: (...args: (string | number | null)[]) => ({
    first: async () => sqlite.prepare(query).get(...args) ?? null,
    all: async () => ({ results: sqlite.prepare(query).all(...args) }),
    run: async () => {
      const { changes } = sqlite.prepare(query).run(...args)
      if (fault.afterIndexCommit && query.includes('INSERT INTO portal_photos')) throw new Error('D1 連線中斷')
      return { success: true, meta: { changes } }
    },
  }) }) } as unknown as D1Database
  const photos = {
    get: async (key: string) => {
      lookupCount++
      const value = objects.get(key)
      if (!value) return null
      return { size: value.byteLength, body: new Blob([value]).stream() }
    },
    put: async (key: string, bytes: ArrayBuffer) => {
      objects.set(key, bytes)
      if (fault.write) throw new Error('R2 連線中斷')
      return { key }
    },
    delete: async (key: string) => {
      if (fault.remove) throw new Error('R2 連線中斷')
      objects.delete(key)
    },
  } as unknown as R2Bucket
  const images = {
    info: async (stream: ReadableStream) => {
      const bytes = Buffer.from(await new Response(stream).arrayBuffer())
      const { format, width, height } = await sharp(bytes).metadata().catch(() => { throw { code: 9412 } })
      return { format: `image/${format}`, width, height, fileSize: bytes.length }
    },
    input: () => ({ transform: () => ({ output: async () => {
      if (fault.transform) throw { code: 9422 }
      return { response: () => new Response(transformed as Uint8Array<ArrayBuffer>, { headers: { 'content-type': 'image/webp' } }) }
    } }) }),
  } as unknown as ImagesBinding
  const invoke = (method: string, user: UserInfo = admin, slot?: string, request?: Request) =>
    handlePhotos(request ?? new Request('https://example.com/api/hosted/photos', { method }), slot === undefined ? path : [...path, slot], user, db, photos, images)
  const upload = (slot = '1', request?: Request) => invoke('PUT', admin, slot, request ?? imageRequest(jpeg))
  const revision = () => [...objects.keys()][0]?.slice('photos/'.length, -'.webp'.length)
  const remove = (v = revision()) => invoke('DELETE', admin, '1', new Request('https://example.com/api/hosted/photos', {
    method: 'DELETE', headers: v ? { 'If-Match': `"${v}"` } : undefined,
  }))
  return { invoke, upload, revision, remove, sqlite, fault, objects, lookups: () => lookupCount, output: (value: Uint8Array<ArrayBuffer>) => { transformed = value } }
}

function imageRequest(body: BodyInit, type = 'image/jpeg', length?: number) {
  const headers = { 'Content-Type': type, ...(length === undefined ? {} : { 'Content-Length': `${length}` }) }
  return new Request('https://example.com/api/hosted/photos', { method: 'PUT', body, headers, duplex: 'half' } as RequestInit)
}

describe('Worker 私人照片', () => {
  it('僅存轉檔後的 WebP；下載與列表維持舊版契約且禁止共用快取', async () => {
    const f = fixture()
    expect((await f.upload()).status).toBe(200)
    expect(await (await f.invoke('GET', member)).json()).toMatchObject({ data: [{ slot: 1, revision: f.revision() }] })
    const download = await f.invoke('GET', member, '1')
    expect(download.headers.get('Cache-Control')).toBe('private, no-store')
    expect(download.headers.get('Cross-Origin-Resource-Policy')).toBe('same-origin')
    expect(download.headers.get('X-Content-Type-Options')).toBe('nosniff')
    expect(download.headers.get('Content-Type')).toBe('image/webp')
    expect(new Uint8Array(await download.arrayBuffer())).toEqual(webp)
    expect(new Uint8Array([...f.objects.values()][0])).not.toEqual(jpeg)
  })

  it('家人無法修改照片，也不能列出或下載隱藏成員的照片', async () => {
    const f = fixture(0)
    expect((await f.upload()).status).toBe(200)
    expect((await f.invoke('PUT', member, '1', imageRequest(jpeg))).status).toBe(403)
    expect((await f.invoke('DELETE', member, '1')).status).toBe(403)
    expect((await f.invoke('GET', member)).status).toBe(404)
    expect((await f.invoke('GET', member, '1')).status).toBe(404)
    expect(f.lookups()).toBe(0)
  })

  it('拒絕成員第二張照片與無效槽位', async () => {
    const f = fixture()
    expect((await f.upload('2')).status).toBe(400)
    expect((await f.upload('01')).status).toBe(400)
    expect(f.objects.size).toBe(0)
  })

  it('拒絕假 JPEG、錯誤宣告、解碼失敗，以及超過 2 MiB 的實際傳輸', async () => {
    const f = fixture()
    expect((await f.upload('1', imageRequest('<svg />'))).status).toBe(415)
    expect((await f.upload('1', imageRequest(jpeg, 'image/png'))).status).toBe(415)
    expect((await f.upload('1', imageRequest(new Uint8Array([255, 216, 255, 0])))).status).toBe(415)
    expect((await f.upload('1', imageRequest(jpeg, 'image/jpeg', 2 * mib + 1))).status).toBe(413)
    const stream = new ReadableStream({ start(controller) { controller.enqueue(new Uint8Array(2 * mib + 1)); controller.close() } })
    expect((await f.upload('1', imageRequest(stream, 'image/jpeg', 10))).status).toBe(413)
    expect(f.objects.size).toBe(0)
  })

  it('拒絕殘留 EXIF、超過尺寸限制或超過 1 MiB 的轉檔結果', async () => {
    const f = fixture()
    f.output(webpWithExif)
    expect((await f.upload()).status).toBe(503)
    const wide = new Uint8Array(await sharp(jpeg).webp().toBuffer())
    f.output(wide)
    expect((await f.upload()).status).toBe(503)
    f.output(new Uint8Array(mib + 1))
    expect((await f.upload()).status).toBe(413)
    expect(f.objects.size).toBe(0)
  })

  it('下載拒絕還原或人工操作後不符合標準的物件', async () => {
    const f = fixture()
    expect((await f.upload()).status).toBe(200)
    const key = [...f.objects.keys()][0]
    f.objects.set(key, webpWithExif.buffer)
    expect((await f.invoke('GET', member, '1')).status).toBe(404)
  })

  it('Images 未提供轉換時拒絕上傳，不儲存原始照片', async () => {
    const f = fixture()
    f.fault.transform = true
    expect((await f.upload()).status).toBe(503)
    expect(f.objects.size).toBe(0)
  })

  it('同槽位競爭只有一張可見；刪照片前 D1 不允許刪紀錄', async () => {
    const f = fixture()
    expect((await Promise.all([f.upload(), f.upload()])).map((r) => r.status).sort()).toEqual([200, 409])
    expect(f.objects.size).toBe(1)
    expect(() => f.sqlite.prepare('DELETE FROM portal_records WHERE id = ?').run(owner)).toThrow(/FOREIGN KEY/)
    expect((await f.remove()).status).toBe(200)
    expect(f.objects.size).toBe(0)
    expect(f.sqlite.prepare('DELETE FROM portal_records WHERE id = ?').run(owner).changes).toBe(1)
  })

  it('物件上傳失敗不留下可見索引，D1 已提交但斷線不會刪除已登記物件', async () => {
    const f = fixture()
    f.fault.write = true
    expect((await f.upload()).status).toBe(503)
    expect(f.objects.size).toBe(0)
    expect(await (await f.invoke('GET')).json()).toMatchObject({ data: [] })
    f.fault.write = false
    f.fault.afterIndexCommit = true
    expect((await f.upload()).status).toBe(200)
    expect((await f.invoke('GET', admin, '1')).status).toBe(200)
  })

  it('R2 刪除失敗時，照片不可下載、仍顯示給管理員重試', async () => {
    const f = fixture()
    await f.upload()
    f.fault.remove = true
    expect((await f.remove()).status).toBe(503)
    expect((await f.invoke('GET', member, '1')).status).toBe(404)
    expect(await (await f.invoke('GET', member)).json()).toMatchObject({ data: [] })
    expect(await (await f.invoke('GET')).json()).toMatchObject({ data: [{ slot: 1, revision: f.revision() }] })
    f.fault.remove = false
    expect((await f.remove()).status).toBe(200)
    expect(f.objects.size).toBe(0)
  })

  it('舊版照片連結或延遲刪除不會影響同位置的替代照片', async () => {
    const f = fixture()
    expect((await f.upload()).status).toBe(200)
    const old = f.revision()
    expect((await f.invoke('DELETE', admin, '1')).status).toBe(428)
    expect((await f.remove(old)).status).toBe(200)
    expect((await f.upload()).status).toBe(200)
    const result = await f.remove(old)
    expect(result.status).toBe(409)
    expect(await result.json()).toMatchObject({ errors: ['VERSION_CONFLICT'] })
    expect(f.revision()).not.toBe(old)
    expect((await f.invoke('GET', member, '1', new Request(`https://example.com/api/hosted/photos?v=${old}`))).status).toBe(404)
    expect((await f.invoke('GET', member, '1')).status).toBe(200)
  })
})
