import { accessConfig, readPortalUser, verifyAccess, type AccessSettings } from './access'
import { portalResponse } from './http'

type RecordArea = 'members' | 'itineraries' | 'worship' | 'events'
type PortalUser = NonNullable<Awaited<ReturnType<typeof readPortalUser>>>

export interface PortalEnv extends AccessSettings {
  DB?: D1Database
  PHOTOS?: R2Bucket
  IMAGES?: ImagesBinding
}

interface PortalDependencies {
  verify: typeof verifyAccess
  readUser: typeof readPortalUser
  records: (request: Request, area: RecordArea, id: string | undefined, user: PortalUser, db: D1Database) => Promise<Response>
  photos: (request: Request, path: string[], user: PortalUser, db: D1Database, photos: R2Bucket, images: ImagesBinding) => Promise<Response>
}

function isRecordArea(area: string): area is RecordArea {
  return area === 'members' || area === 'itineraries' || area === 'worship' || area === 'events'
}

export function createPortal(deps: PortalDependencies) {
  return async (request: Request, env: PortalEnv): Promise<Response> => {
    const config = accessConfig(env)
    if (!config || !env.DB) return portalResponse(503, '家族入口尚未設定')

    const url = new URL(request.url)
    if (url.origin !== config.origin) return portalResponse(403, '請使用家族入口的正式網址')
    if (url.pathname.length > 512 || url.search.length > 4096) return portalResponse(414, '網址超過大小限制')
    const route = /^\/api\/hosted\/([a-z]+)((?:\/[a-zA-Z0-9-]+)*)$/.exec(url.pathname)
    if (!route) return portalResponse(404, '找不到功能')

    const read = request.method === 'GET'
    if (!read && request.method !== 'POST' && request.method !== 'PUT' && request.method !== 'DELETE') {
      return portalResponse(405, '不支援此操作')
    }
    if (!read && request.headers.get('Origin') !== config.origin) return portalResponse(403, '不接受其他網站的操作')

    try {
      const identity = await deps.verify(request.headers.get('Cf-Access-Jwt-Assertion'), config)
      if (!identity) return portalResponse(401, '請使用 Cloudflare Access 登入')
      const user = await deps.readUser(env.DB, identity)
      if (!user) return portalResponse(403, '此帳號沒有家人存取資格')
      if (!read && user.role !== 'Admin') return portalResponse(403, '只有管理員可以更動家族資料')

      const [, area, rest] = route
      const path = rest.slice(1).split('/').filter(Boolean)
      if (area === 'auth' && path.length === 1 && path[0] === 'me' && read) return portalResponse(200, '登入成功', user)
      if (isRecordArea(area) && path.length <= 1) return await deps.records(request, area, path[0], user, env.DB)
      if (area === 'photos' && path.length >= 2 && path.length <= 3) {
        if (!env.PHOTOS || !env.IMAGES) return portalResponse(503, '照片服務尚未設定')
        return await deps.photos(request, path, user, env.DB, env.PHOTOS, env.IMAGES)
      }
      return portalResponse(404, '找不到功能')
    } catch {
      // 不傳回例外訊息，避免暴露家族資料、服務帳號或憑證。
      return portalResponse(503, '家族服務暫時無法使用，請稍後再試')
    }
  }
}
