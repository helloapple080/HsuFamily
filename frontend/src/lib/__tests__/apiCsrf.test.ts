import axios, {
  AxiosError,
  AxiosHeaders,
  type AxiosAdapter,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from 'axios'
import { afterEach, describe, expect, it, vi } from 'vitest'

const originalAxiosAdapter = axios.defaults.adapter

interface RecordedRequest {
  method: string
  token: string | null
  url: string
}

interface Deferred<T> {
  promise: Promise<T>
  resolve: (value: T) => void
}

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}

function response(
  config: InternalAxiosRequestConfig,
  data: unknown,
  status = 200
): AxiosResponse {
  return {
    config,
    data,
    headers: new AxiosHeaders(),
    status,
    statusText: status >= 400 ? 'Error' : 'OK',
  }
}

function reject(
  config: InternalAxiosRequestConfig,
  status: number
): Promise<AxiosResponse> {
  const failedResponse = response(config, {}, status)
  return Promise.reject(new AxiosError(
    `HTTP ${status}`,
    'ERR_BAD_RESPONSE',
    config,
    undefined,
    failedResponse
  ))
}

async function loadApi(adapter: AxiosAdapter) {
  vi.resetModules()
  axios.defaults.adapter = adapter
  return import('@/lib/api')
}

function record(config: InternalAxiosRequestConfig): RecordedRequest {
  const token = AxiosHeaders.from(config.headers).get('X-XSRF-TOKEN')
  return {
    method: config.method ?? 'get',
    token: typeof token === 'string' ? token : null,
    url: config.url ?? '',
  }
}

describe('API CSRF identity boundaries', () => {
  afterEach(() => {
    axios.defaults.adapter = originalAxiosAdapter
    vi.resetModules()
    vi.restoreAllMocks()
  })

  it('fetches a fresh token after a successful login changes identity', async () => {
    const requests: RecordedRequest[] = []
    const csrfTokens = ['anonymous-token', 'admin-token']
    const adapter: AxiosAdapter = async (config) => {
      requests.push(record(config))
      if (config.url === '/api/auth/csrf') {
        return response(config, {
          success: true,
          data: { token: csrfTokens.shift() },
        })
      }
      return response(config, { success: true })
    }
    const { default: api } = await loadApi(adapter)

    await api.post('/api/probe')
    await api.post('/api/auth/login', { username: 'admin', password: 'secret' })
    await api.patch('/api/media/media-1/visibility', { visibility: 'Public' })

    const csrfRequests = requests.filter((request) => request.url === '/api/auth/csrf')
    expect(csrfRequests).toHaveLength(2)
    expect(requests.find((request) => request.url.includes('/visibility'))?.token)
      .toBe('admin-token')
  })

  it('invalidates the token after refresh before replaying a failed mutation', async () => {
    const requests: RecordedRequest[] = []
    const csrfTokens = ['pre-refresh-token', 'post-refresh-token']
    let mutationAttempts = 0
    const adapter: AxiosAdapter = async (config) => {
      requests.push(record(config))
      if (config.url === '/api/auth/csrf') {
        return response(config, {
          success: true,
          data: { token: csrfTokens.shift() },
        })
      }
      if (config.url === '/api/media/media-1/visibility' && mutationAttempts++ === 0) {
        return reject(config, 401)
      }
      return response(config, { success: true })
    }
    const { default: api } = await loadApi(adapter)

    await api.patch('/api/media/media-1/visibility', { visibility: 'Public' })

    const mutations = requests.filter((request) => request.url.includes('/visibility'))
    assertRequestTokens(mutations, ['pre-refresh-token', 'post-refresh-token'])
    expect(requests.find((request) => request.url === '/api/auth/refresh')?.token)
      .toBe('pre-refresh-token')
  })

  it.each(['success', 'error'] as const)(
    'clears the cached token on logout %s',
    async (logoutResult) => {
      const requests: RecordedRequest[] = []
      const csrfTokens = ['signed-in-token', 'anonymous-token']
      const adapter: AxiosAdapter = async (config) => {
        requests.push(record(config))
        if (config.url === '/api/auth/csrf') {
          return response(config, {
            success: true,
            data: { token: csrfTokens.shift() },
          })
        }
        if (config.url === '/api/auth/logout' && logoutResult === 'error') {
          return reject(config, 500)
        }
        return response(config, { success: true })
      }
      const { default: api } = await loadApi(adapter)

      await api.post('/api/probe')
      await api.post('/api/auth/logout').catch(() => undefined)
      await api.post('/api/after-logout')

      expect(requests.find((request) => request.url === '/api/after-logout')?.token)
        .toBe('anonymous-token')
    }
  )

  it('does not let a stale in-flight token request repopulate the cache', async () => {
    const requests: RecordedRequest[] = []
    const stale = deferred<AxiosResponse>()
    let csrfCalls = 0
    const adapter: AxiosAdapter = async (config) => {
      requests.push(record(config))
      if (config.url !== '/api/auth/csrf') {
        return response(config, { success: true })
      }

      csrfCalls++
      if (csrfCalls === 1) return stale.promise
      return response(config, { success: true, data: { token: 'fresh-token' } })
    }
    const { default: api, invalidateCsrfToken } = await loadApi(adapter)

    const beforeIdentityChange = api.post('/api/before-change')
    await vi.waitFor(() => expect(csrfCalls).toBe(1))
    invalidateCsrfToken()
    await api.post('/api/after-change')
    stale.resolve(response(
      { headers: new AxiosHeaders(), url: '/api/auth/csrf' } as InternalAxiosRequestConfig,
      { success: true, data: { token: 'stale-token' } }
    ))
    await beforeIdentityChange
    await api.post('/api/final')

    expect(requests.find((request) => request.url === '/api/final')?.token)
      .toBe('fresh-token')
    expect(csrfCalls).toBe(2)
  })
})

function assertRequestTokens(requests: RecordedRequest[], expected: Array<string | null>): void {
  expect(requests.map((request) => request.token)).toEqual(expected)
}
