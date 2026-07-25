import { afterEach, describe, expect, it, vi } from 'vitest'

describe('Demo 模式設定', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
    vi.resetModules()
  })

  it('只在環境變數明確設為 true 時啟用', async () => {
    vi.stubEnv('NEXT_PUBLIC_DEMO_MODE', 'true')

    const config = await import('../config')

    expect(config.isDemoMode).toBe(true)
    expect(config.demoBasePath).toBe('/HsuFamily')
  })

  it('一般建置預設維持正式模式', async () => {
    vi.stubEnv('NEXT_PUBLIC_DEMO_MODE', 'false')

    const config = await import('../config')

    expect(config.isDemoMode).toBe(false)
    expect(config.demoBasePath).toBe('')
  })
})
