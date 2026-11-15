import { z } from 'zod'
import { maxPhotoSlots, type PhotoKind } from '@/lib/hosted/photo-contract'
import type { UserInfo } from '@/types/auth'
import { portalResponse } from './http'

const maxUploadBytes = 2 * 1024 * 1024
const maxStoredBytes = 1024 * 1024
const maxInputPixels = 24_000_000
const maxOutputSide = 1280
const recordId = z.uuid()
const photoKey = /^photos\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\.webp$/
const revisionOf = (key: string) => photoKey.exec(key)?.[1]
const invalidPhoto = () => portalResponse(415, '僅支援有效的靜態 JPEG、PNG 或 WebP 圖片')
const largePhoto = () => portalResponse(413, '圖片超過容量上限，請先縮小檔案')
const unavailable = () => portalResponse(503, '照片服務暫時無法使用，請稍後重試')
const occupied = () => portalResponse(409, '這個照片位置已被使用')

interface PhotoRow { object_key: string; state: 'ready' | 'deleting' }

class LimitExceeded extends Error {}

async function readBounded(source: Request | Response, limit: number): Promise<Uint8Array<ArrayBuffer>> {
  if (Number(source.headers.get('content-length')) > limit) {
    await source.body?.cancel().catch(() => undefined)
    throw new LimitExceeded()
  }
  if (!source.body) return new Uint8Array(0)
  const reader = source.body.getReader()
  const parts: Uint8Array[] = []
  let length = 0
  try {
    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      length += value.byteLength
      if (length > limit) {
        await reader.cancel().catch(() => undefined)
        throw new LimitExceeded()
      }
      parts.push(value)
    }
  } finally { reader.releaseLock() }
  const bytes = new Uint8Array(length)
  let offset = 0
  for (const part of parts) {
    bytes.set(part, offset)
    offset += part.byteLength
  }
  return bytes
}

const fourCC = (bytes: Uint8Array, at: number) => String.fromCharCode(bytes[at], bytes[at + 1], bytes[at + 2], bytes[at + 3])
const isWebp = (bytes: Uint8Array) => bytes.length >= 12 && fourCC(bytes, 0) === 'RIFF' && fourCC(bytes, 8) === 'WEBP'
const withinSideLimit = (width: number, height: number) => width > 0 && height > 0 && width <= maxOutputSide && height <= maxOutputSide
const pngHeader = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]

function matchesStaticFormat(bytes: Uint8Array, format: string) {
  if (format === 'image/jpeg') return bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff
  if (format === 'image/png' && pngHeader.every((v, i) => v === bytes[i])) {
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
    for (let offset = 8; offset + 12 <= bytes.length;) {
      const tag = fourCC(bytes, offset + 4)
      if (tag === 'acTL') return false
      if (tag === 'IDAT') return true
      offset += 12 + view.getUint32(offset)
    }
  }
  if (format === 'image/webp' && isWebp(bytes) && bytes.length >= 20) {
    const first = fourCC(bytes, 12)
    return first === 'VP8 ' || first === 'VP8L' || (first === 'VP8X' && bytes.length >= 30 && (bytes[20] & 0x02) === 0)
  }
  return false
}

// 僅接受靜態影像的標準位元區塊；EXIF、GPS、XMP 或額外資料均不得進入 R2。
function isSafeWebp(bytes: Uint8Array) {
  if (!isWebp(bytes)) return false
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
  if (view.getUint32(4, true) !== bytes.length - 8) return false
  let canvas: [number, number] | undefined
  let alpha = false
  for (let offset = 12; offset + 8 <= bytes.length;) {
    const tag = fourCC(bytes, offset)
    const length = view.getUint32(offset + 4, true)
    const data = offset + 8
    const next = data + length + (length % 2)
    if (next > bytes.length) return false
    if (tag === 'VP8X' && offset === 12 && length === 10 && (bytes[data] & ~0x10) === 0) {
      canvas = [1 + view.getUint16(data + 4, true) + bytes[data + 6] * 65_536,
        1 + view.getUint16(data + 7, true) + bytes[data + 9] * 65_536]
      if (!withinSideLimit(...canvas)) return false
    } else if (tag === 'ALPH' && canvas && !alpha && (bytes[20] & 0x10) !== 0 && length > 0) {
      alpha = true
    } else if (tag === 'VP8 ' && next === bytes.length && length >= 10 && (bytes[data] & 1) === 0 &&
      bytes[data + 3] === 0x9d && bytes[data + 4] === 0x01 && bytes[data + 5] === 0x2a) {
      return withinSideLimit(view.getUint16(data + 6, true) & 0x3fff, view.getUint16(data + 8, true) & 0x3fff)
    } else if (tag === 'VP8L' && next === bytes.length && length >= 5 && bytes[data] === 0x2f && !alpha) {
      const bits = view.getUint32(data + 1, true)
      return withinSideLimit((bits & 0x3fff) + 1, ((bits >>> 14) & 0x3fff) + 1)
    } else {
      return false
    }
    offset = next
  }
  return false
}

function streamFrom(bytes: Uint8Array<ArrayBuffer>): ReadableStream<Uint8Array> {
  return new ReadableStream({ start(controller) { controller.enqueue(bytes); controller.close() } })
}

const imageErrorCode = (error: unknown) => typeof error === 'object' && error !== null && 'code' in error ? error.code : undefined

async function encodePhoto(request: Request, images: ImagesBinding): Promise<Response | ArrayBuffer> {
  const format = request.headers.get('content-type')?.toLowerCase()
  if (format !== 'image/jpeg' && format !== 'image/png' && format !== 'image/webp') return invalidPhoto()
  let input: Uint8Array<ArrayBuffer>
  try { input = await readBounded(request, maxUploadBytes) } catch (error) {
    return error instanceof LimitExceeded ? largePhoto() : invalidPhoto()
  }
  if (!matchesStaticFormat(input, format)) return invalidPhoto()
  try {
    const info = await images.info(streamFrom(input))
    if (info.format !== format || !('width' in info) || !Number.isSafeInteger(info.width) ||
      !Number.isSafeInteger(info.height) || info.width < 1 || info.height < 1) return invalidPhoto()
    if (info.width * info.height > maxInputPixels) return largePhoto()
    const result = (await images.input(streamFrom(input))
      .transform({ width: maxOutputSide, height: maxOutputSide, fit: 'scale-down' })
      .output({ format: 'image/webp', quality: 78, anim: false })).response()
    if (!result.ok || result.headers.get('content-type') !== 'image/webp') {
      await result.body?.cancel().catch(() => undefined)
      return unavailable()
    }
    const output = await readBounded(result, maxStoredBytes)
    return isSafeWebp(output) ? output.buffer : unavailable()
  } catch (error) {
    if (error instanceof LimitExceeded) return largePhoto()
    const code = imageErrorCode(error)
    return code === 9412 || code === 9406 ? invalidPhoto() : unavailable()
  }
}

async function removeObject(photos: R2Bucket, key: string) {
  try { await photos.delete(key) } catch { /* 留待離線對帳；不公開物件或記錄私人路徑。 */ }
}

async function uploadPhoto(request: Request, kind: PhotoKind, id: string, slot: number, user: UserInfo,
  db: D1Database, photos: R2Bucket, images: ImagesBinding) {
  const current = await db.prepare('SELECT slot FROM portal_photos WHERE kind = ? AND record_id = ? AND slot = ?')
    .bind(kind, id, slot).first()
  if (current) return occupied()
  const bytes = await encodePhoto(request, images)
  if (bytes instanceof Response) return bytes
  const key = `photos/${crypto.randomUUID()}.webp`
  try {
    await photos.put(key, bytes, { httpMetadata: { contentType: 'image/webp', cacheControl: 'private, no-store' } })
  } catch {
    await removeObject(photos, key)
    return unavailable()
  }
  try {
    // 只有目的紀錄仍存在，且此槽位未被其他請求搶先佔用，才公開索引。
    const inserted = await db.prepare(`INSERT INTO portal_photos (kind, record_id, slot, object_key, state, created_by)
      SELECT kind, id, ?, ?, 'ready', ? FROM portal_records WHERE kind = ? AND id = ?
      ON CONFLICT(kind, record_id, slot) DO NOTHING`).bind(slot, key, user.id, kind, id).run()
    if (!inserted.success) throw new Error('照片索引寫入未確認')
    if (inserted.meta.changes === 1) return portalResponse(200, '照片已儲存', true)
  } catch {
    // 連線錯誤不表示 D1 一定沒有提交。無法確認時不能刪除可能已登記的物件。
    try {
      if (await db.prepare('SELECT 1 FROM portal_photos WHERE object_key = ?').bind(key).first()) {
        return portalResponse(200, '照片已儲存', true)
      }
    } catch { return unavailable() }
    await removeObject(photos, key)
    return unavailable()
  }
  await removeObject(photos, key)
  return occupied()
}

async function deletePhoto(request: Request, kind: PhotoKind, id: string, slot: number, db: D1Database, photos: R2Bucket) {
  const match = request.headers.get('If-Match')
  const revision = match?.startsWith('"') && match.endsWith('"') ? match.slice(1, -1) : undefined
  if (!recordId.safeParse(revision).success) return portalResponse(428, '請重新載入照片後再刪除')
  const row = await db.prepare('SELECT object_key, state FROM portal_photos WHERE kind = ? AND record_id = ? AND slot = ?')
    .bind(kind, id, slot).first<PhotoRow>()
  if (!row) return portalResponse(200, '照片已刪除', true)
  const current = revisionOf(row.object_key)
  if (!current) return unavailable()
  if (revision !== current) return portalResponse(409, '照片已更新，請重新確認後再刪除', null, ['VERSION_CONFLICT'])
  // 先阻擋一般讀取，失敗時讓管理員重試。同槽位的新照片會有不同、不可重用的物件路徑。
  const marked = await db.prepare("UPDATE portal_photos SET state = 'deleting' WHERE object_key = ?")
    .bind(row.object_key).run()
  if (!marked.success) return unavailable()
  await photos.delete(row.object_key)
  const removed = await db.prepare("DELETE FROM portal_photos WHERE object_key = ? AND state = 'deleting'")
    .bind(row.object_key).run()
  return removed.success ? portalResponse(200, '照片已刪除', true) : unavailable()
}

export async function handlePhotos(request: Request, path: readonly string[], user: UserInfo, db: D1Database,
  photos: R2Bucket, images: ImagesBinding): Promise<Response> {
  if (user.role !== 'Admin' && user.role !== 'User') return portalResponse(403, '不允許存取照片')
  if (request.method !== 'GET' && user.role !== 'Admin') return portalResponse(403, '只有管理員可以更動照片')
  const [kind, rawId, rawSlot] = path
  const id = rawId?.toLowerCase()
  if (path.length < 2 || path.length > 3 ||
    (kind !== 'members' && kind !== 'itineraries' && kind !== 'worship') || !id || !recordId.safeParse(id).success) {
    return portalResponse(400, '照片所屬紀錄不正確')
  }
  const slot = rawSlot === undefined ? undefined : Number(rawSlot)
  if (rawSlot !== undefined && (!/^[1-5]$/.test(rawSlot) || slot === undefined || slot > maxPhotoSlots(kind))) {
    return portalResponse(400, '照片位置不正確')
  }
  try {
    const isAdmin = Number(user.role === 'Admin')
    const target = await db.prepare('SELECT id FROM portal_records WHERE kind = ? AND id = ? AND (? = 1 OR active = 1)')
      .bind(kind, id, isAdmin).first()
    if (!target) return portalResponse(404, '找不到照片所屬紀錄')
    if (request.method === 'GET') {
      if (slot === undefined) {
        const { results } = await db.prepare(`SELECT p.slot, p.object_key FROM portal_photos p
          JOIN portal_records r ON p.kind = r.kind AND p.record_id = r.id
          WHERE p.kind = ? AND p.record_id = ? AND (? = 1 OR (r.active = 1 AND p.state = 'ready')) ORDER BY p.slot`)
          .bind(kind, id, isAdmin).all<{ slot: number; object_key: string }>()
        return portalResponse(200, '查詢成功', results.flatMap(({ slot, object_key }) => {
          const revision = revisionOf(object_key)
          return revision ? [{ slot, revision }] : []
        }))
      }
      const row = await db.prepare(`SELECT p.object_key FROM portal_photos p
        JOIN portal_records r ON p.kind = r.kind AND p.record_id = r.id
        WHERE p.kind = ? AND p.record_id = ? AND p.slot = ? AND p.state = 'ready' AND (? = 1 OR r.active = 1)`)
        .bind(kind, id, slot, isAdmin).first<PhotoRow>()
      const current = row && revisionOf(row.object_key)
      const requested = new URL(request.url).searchParams.get('v')
      const photo = row && current && (requested === null || requested === current) ? await photos.get(row.object_key) : null
      if (!photo || photo.size < 12 || photo.size > maxStoredBytes) {
        await photo?.body.cancel().catch(() => undefined)
        return portalResponse(404, '找不到照片')
      }
      let bytes: Uint8Array<ArrayBuffer>
      try { bytes = await readBounded(new Response(photo.body), maxStoredBytes) } catch (error) {
        if (error instanceof LimitExceeded) return portalResponse(404, '找不到照片')
        return unavailable()
      }
      if (bytes.byteLength !== photo.size || !isSafeWebp(bytes)) return portalResponse(404, '找不到照片')
      return new Response(bytes, { headers: {
        'Content-Type': 'image/webp', 'Cache-Control': 'private, no-store',
        'Cross-Origin-Resource-Policy': 'same-origin', 'X-Content-Type-Options': 'nosniff',
      } })
    }
    if (slot === undefined) return portalResponse(405, '請指定照片位置')
    if (request.method === 'PUT') return await uploadPhoto(request, kind, id, slot, user, db, photos, images)
    if (request.method === 'DELETE') return await deletePhoto(request, kind, id, slot, db, photos)
    return portalResponse(405, '不支援的操作')
  } catch { return unavailable() }
}
