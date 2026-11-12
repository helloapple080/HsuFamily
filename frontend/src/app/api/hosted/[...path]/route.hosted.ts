import { isHostedMode } from '@/lib/hosted/mode'
import { handleHostedRequest, portalResponse } from '@/lib/hosted/portal'
import { createHostedGateway, getHostedConfig } from '@/lib/hosted/supabase'

interface RouteContext {
  params: Promise<{ path: string[] }>
}

async function handle(request: Request, context: RouteContext) {
  if (!isHostedMode) return portalResponse(404, '找不到功能')
  const config = getHostedConfig()
  if (!config) return portalResponse(503, '精簡版尚未完成設定')

  const { path } = await context.params
  const session = await createHostedGateway(config)
  const response = await handleHostedRequest(request, path, config.origin, async () => session.gateway)
  const deniedLogin = path.join('/') === 'auth/login' && !response.ok
  if (!deniedLogin && response.status < 500) session.commitCookies()
  return response
}

export const GET = handle
export const POST = handle
export const PUT = handle
export const DELETE = handle
