import { AxiosError, AxiosHeaders, CanceledError } from 'axios'
import { describe, expect, it } from 'vitest'
import { shouldRetryQuery } from '@/lib/queryRetry'

function axiosStatus(status: number): AxiosError {
  return new AxiosError('request failed', 'ERR_BAD_RESPONSE', undefined, undefined, {
    status,
    statusText: '',
    headers: {},
    config: { headers: new AxiosHeaders() },
    data: {},
  })
}

describe('shouldRetryQuery', () => {
  it.each([400, 401, 403, 404, 422])('does not retry HTTP %s', (status) => {
    expect(shouldRetryQuery(0, axiosStatus(status))).toBe(false)
  })

  it('retries a server error at most twice', () => {
    expect(shouldRetryQuery(0, axiosStatus(503))).toBe(true)
    expect(shouldRetryQuery(1, axiosStatus(503))).toBe(true)
    expect(shouldRetryQuery(2, axiosStatus(503))).toBe(false)
  })

  it('retries a network error at most twice', () => {
    const networkError = new AxiosError('network unavailable', 'ERR_NETWORK')

    expect(shouldRetryQuery(0, networkError)).toBe(true)
    expect(shouldRetryQuery(2, networkError)).toBe(false)
  })

  it('does not retry a cancelled request', () => {
    expect(shouldRetryQuery(0, new CanceledError('request cancelled'))).toBe(false)
  })

  it('does not retry a non-Axios application error', () => {
    expect(shouldRetryQuery(0, new Error('invalid response envelope'))).toBe(false)
  })

  it('only retries HTTP responses in the 5xx range', () => {
    expect(shouldRetryQuery(0, axiosStatus(500))).toBe(true)
    expect(shouldRetryQuery(0, axiosStatus(599))).toBe(true)
    expect(shouldRetryQuery(0, axiosStatus(600))).toBe(false)
  })
})
