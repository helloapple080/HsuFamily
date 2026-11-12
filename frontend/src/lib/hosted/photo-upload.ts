import sharp from 'sharp'

export const photoLimit = 2 * 1024 * 1024
export const storedPhotoLimit = 1024 * 1024
const pixelLimit = 24_000_000
const formats = new Map([['image/png', 'png'], ['image/jpeg', 'jpeg'], ['image/webp', 'webp']])
const pngHeader = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])

export function isWebp(data: Buffer) {
  return data.length >= 12 && data.toString('ascii', 0, 4) === 'RIFF' && data.toString('ascii', 8, 12) === 'WEBP'
}

export async function encodePhoto(request: Request): Promise<
  { ok: true; bytes: Buffer } | { ok: false; status: 413 | 415; message: string }
> {
  const invalid = { ok: false, status: 415, message: '僅支援有效的靜態 JPEG、PNG 或 WebP 圖片' } as const
  const large = { ok: false, status: 413, message: '圖片不可超過 2 MiB，請先縮小檔案' } as const
  const expected = formats.get(request.headers.get('content-type')?.toLowerCase() ?? '')
  if (!expected || !request.body) return invalid
  if (Number(request.headers.get('content-length')) > photoLimit) return large
  const reader = request.body.getReader()
  const chunks: Uint8Array[] = []
  let size = 0
  try {
    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      size += value.byteLength
      if (size > photoLimit) {
        await reader.cancel().catch(() => undefined)
        return large
      }
      chunks.push(value)
    }
    const input = Buffer.concat(chunks, size)
    // 在解碼前阻擋 SVG 等不支援格式，不信任副檔名或 Content-Type。
    const signature = expected === 'png' ? input.subarray(0, 8).equals(pngHeader)
      : expected === 'webp' ? isWebp(input)
        : input[0] === 0xff && input[1] === 0xd8 && input[2] === 0xff
    if (!signature) return invalid
    const image = sharp(input, { limitInputPixels: pixelLimit, failOn: 'warning' })
    const meta = await image.metadata()
    if (meta.format !== expected || (meta.pages ?? 1) !== 1) return invalid
    // 重新編碼會丟棄 EXIF/GPS；先按原方向轉正再縮圖。
    const bytes = await image.rotate().resize(1280, 1280, { fit: 'inside', withoutEnlargement: true }).webp({ quality: 78 }).toBuffer()
    return bytes.length > storedPhotoLimit ? large : { ok: true, bytes }
  } catch {
    return invalid
  } finally {
    reader.releaseLock()
  }
}
