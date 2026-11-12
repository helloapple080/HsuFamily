import type { SupabaseClient } from '@supabase/supabase-js'
import { createHmac } from 'node:crypto'
import { maxPhotoSlots, type PhotoKind } from './photo-contract'
import { isWebp, storedPhotoLimit } from './photo-upload'

const folder = (kind: PhotoKind, id: string) => `${kind}/${id}`
const objectKey = (kind: PhotoKind, id: string, slot: number) => `${folder(kind, id)}/${slot}.webp`

export function createPhotoStore(client: SupabaseClient) {
  const bucket = client.storage.from('family-photos')
  return {
    async targetExists(kind: PhotoKind, id: string) {
      const query = client.from(kind === 'worship' ? 'worship_rules' : 'family_records').select('id').eq('id', id)
      if (kind !== 'worship') query.eq('kind', kind)
      const { data, error } = await query.maybeSingle()
      if (error) throw error
      return data !== null
    },
    async list(kind: PhotoKind, id: string) {
      const { data, error } = await bucket.list(folder(kind, id), { limit: 10 })
      if (error) throw error
      return data.flatMap((item) => {
        const slot = Number(item.name.match(/^([1-5])\.webp$/)?.[1])
        return slot <= maxPhotoSlots(kind) ? [slot] : []
      }).sort((a, b) => a - b)
    },
    async put(kind: PhotoKind, id: string, slot: number, bytes: Buffer, author: string) {
      const key = process.env.PORTAL_PHOTO_UPLOAD_KEY
      if (!key || !/^[0-9a-f]{64}$/i.test(key)) throw new Error('照片上傳未完成設定')
      const object = objectKey(kind, id, slot)
      const expires = Math.floor(Date.now() / 1000) + 60
      const mac = createHmac('sha256', Buffer.from(key, 'hex')).update(`${author}|${object}|${expires}`).digest('hex')
      const { error } = await bucket.upload(object, bytes, {
        upsert: false, contentType: 'image/webp', cacheControl: '0', headers: { 'x-portal-upload': `${expires}.${mac}` },
      })
      if (error && (error.status === 409 || ['409', 'ResourceAlreadyExists', 'KeyAlreadyExists', 'Duplicate'].includes(error.statusCode ?? ''))) return false
      if (error) throw error
      return true
    },
    async get(kind: PhotoKind, id: string, slot: number) {
      const { data, error } = await bucket.download(objectKey(kind, id, slot))
      if (error) {
        if (error.status === 404 || ['404', 'NoSuchKey', 'NoSuchObject'].includes(error.statusCode ?? '')) return null
        throw error
      }
      if (data.size > storedPhotoLimit || !isWebp(Buffer.from(await data.slice(0, 12).arrayBuffer()))) return null
      return data
    },
    async remove(kind: PhotoKind, id: string, slot?: number) {
      const slots = slot ? [slot] : Array.from({ length: maxPhotoSlots(kind) }, (_, index) => index + 1)
      const { error } = await bucket.remove(slots.map((key) => objectKey(kind, id, key)))
      if (error) throw error
    },
  }
}

export type PhotoStore = ReturnType<typeof createPhotoStore>
