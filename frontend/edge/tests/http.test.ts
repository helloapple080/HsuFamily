// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { portalResponse, readJson, requireVersion } from '../http'

function request(body?: string, headers?: Record<string, string>) {
  return new Request('https://family.example.com/api/hosted/worship', { method: 'PUT', body, headers })
}

describe('私人 API 請求與回應', () => {
  it('只接受單一且正整數的強 ETag', () => {
    expect(requireVersion(request(undefined, { 'If-Match': '"12"' }))).toBe(12)
    for (const value of ['"0"', '12', 'W/"12"', '*', '"3", "4"', '"99999999999999999"']) {
      expect(requireVersion(request(undefined, { 'If-Match': value }))).toBeNull()
    }
  })

  it('不信任長度標頭，實際讀取有上限，只接受合法 JSON', async () => {
    const headers = { 'content-type': 'application/json' }
    expect(await readJson(request('{"name":"家"}', headers))).toEqual({ ok: true, value: { name: '家' } })
    const invalid = await readJson(request('not json', headers))
    const oversized = await readJson(request('"12345"', { ...headers, 'content-length': '3' }), 5)
    const wrongType = await readJson(request('"hello"'))
    expect(invalid.ok ? null : invalid.response.status).toBe(400)
    expect(oversized.ok ? null : oversized.response.status).toBe(413)
    expect(wrongType.ok ? null : wrongType.response.status).toBe(415)
  })

  it('錯誤也不快取，並可傳回供前端判斷的錯誤代碼', async () => {
    const response = portalResponse(409, '資料已更新', null, ['VERSION_CONFLICT'])
    expect(response.headers.get('cache-control')).toBe('private, no-store')
    expect(response.headers.get('x-content-type-options')).toBe('nosniff')
    expect(await response.json()).toEqual({ success: false, message: '資料已更新', data: null, errors: ['VERSION_CONFLICT'] })
  })
})
