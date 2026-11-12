// @vitest-environment node
import sharp from 'sharp'
import { describe, expect, it } from 'vitest'
import { encodePhoto, photoLimit } from '../photo-upload'

function imageRequest(body: Uint8Array<ArrayBuffer>, mime = 'image/png') {
  return new Request('https://family.example.test', { method: 'PUT', headers: { 'Content-Type': mime }, body })
}

describe('私人圖片上傳', () => {
  it('重新編碼有效圖檔，壓縮尺寸並移除原始 EXIF', async () => {
    const png = await sharp({ create: { width: 1400, height: 600, channels: 3, background: 'red' } })
      .withExif({ IFD0: { Artist: 'private marker' } }).png().toBuffer()
    const result = await encodePhoto(imageRequest(Uint8Array.from(png)))
    expect(result.ok).toBe(true)
    if (!result.ok) return
    const metadata = await sharp(result.bytes).metadata()
    expect(metadata).toMatchObject({ format: 'webp', width: 1280 })
    expect(metadata.exif).toBeUndefined()
  })

  it('阻擋假圖片、宣告類型錯誤與超過 2 MiB 的資料', async () => {
    await expect(encodePhoto(imageRequest(new TextEncoder().encode('<svg/>')))).resolves.toMatchObject({ status: 415 })
    const png = await sharp({ create: { width: 2, height: 2, channels: 3, background: 'red' } }).png().toBuffer()
    await expect(encodePhoto(imageRequest(Uint8Array.from(png), 'image/jpeg'))).resolves.toMatchObject({ status: 415 })
    await expect(encodePhoto(imageRequest(new Uint8Array(photoLimit + 1)))).resolves.toMatchObject({ status: 413 })
  })
})
