import type { ReactNode } from 'react'
import { StrictMode } from 'react'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '@/stores/authStore'
import { useSignalR } from '@/hooks/useSignalR'

const signalRMock = vi.hoisted(() => {
  const states = {
    Disconnected: 'Disconnected',
    Connecting: 'Connecting',
    Connected: 'Connected',
    Disconnecting: 'Disconnecting',
    Reconnecting: 'Reconnecting',
  } as const

  class FakeConnection {
    state: string = states.Disconnected
    handlers = new Map<string, Set<(...args: unknown[]) => void>>()
    reconnectingHandler: (() => void) | undefined
    reconnectedHandler: (() => void | Promise<void>) | undefined
    closeHandler: (() => void) | undefined
    startGate: Promise<void> | null = null
    start = vi.fn(async () => {
      this.state = states.Connecting
      await this.startGate
      this.state = states.Connected
    })
    stop = vi.fn(async () => {
      this.state = states.Disconnecting
      this.state = states.Disconnected
    })
    on = vi.fn((event: string, callback: (...args: unknown[]) => void) => {
      const callbacks = this.handlers.get(event) ?? new Set()
      callbacks.add(callback)
      this.handlers.set(event, callbacks)
    })
    off = vi.fn((event: string, callback: (...args: unknown[]) => void) => {
      this.handlers.get(event)?.delete(callback)
    })
    onreconnecting = vi.fn((callback: () => void) => { this.reconnectingHandler = callback })
    onreconnected = vi.fn((callback: () => void | Promise<void>) => { this.reconnectedHandler = callback })
    onclose = vi.fn((callback: () => void) => { this.closeHandler = callback })
    emit(event: string, ...args: unknown[]) {
      this.handlers.get(event)?.forEach((callback) => callback(...args))
    }
  }

  const connections: FakeConnection[] = []
  const withUrl = vi.fn()
  let nextStartGate: Promise<void> | null = null
  const setNextStartGate = (gate: Promise<void> | null) => { nextStartGate = gate }
  class Builder {
    withUrl(url: string, options: unknown) { withUrl(url, options); return this }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() {
      const connection = new FakeConnection()
      connection.startGate = nextStartGate
      nextStartGate = null
      connections.push(connection)
      return connection
    }
  }

  return { Builder, connections, states, withUrl, setNextStartGate }
})

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: signalRMock.Builder,
  HubConnectionState: signalRMock.states,
  LogLevel: { Warning: 3 },
}))

function createWrapper(queryClient: QueryClient, strict = false) {
  return function Wrapper({ children }: { children: ReactNode }) {
    const content = <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    return strict ? <StrictMode>{content}</StrictMode> : content
  }
}

function authenticate() {
  useAuthStore.setState({
    user: { id: '1', username: 'admin', email: 'admin@example.com', displayName: '許浴宸', role: 'Admin' },
    isAuthenticated: true,
    isAdmin: true,
    isHydrated: true,
  })
}

describe('useSignalR', () => {
  beforeEach(async () => {
    useAuthStore.getState().logout()
    signalRMock.connections.length = 0
    signalRMock.withUrl.mockClear()
    signalRMock.setNextStartGate(null)
    await act(async () => { await Promise.resolve() })
  })

  it('does not build a connection before authentication', async () => {
    const queryClient = new QueryClient()
    const { result, unmount } = renderHook(() => useSignalR(), { wrapper: createWrapper(queryClient) })
    expect(result.current.status).toBe('idle')
    expect(signalRMock.connections).toHaveLength(0)
    unmount()
  })

  it('shares one cookie-authenticated connection under StrictMode', async () => {
    authenticate()
    const queryClient = new QueryClient()
    const first = renderHook(() => useSignalR(), { wrapper: createWrapper(queryClient, true) })
    const second = renderHook(() => useSignalR(), { wrapper: createWrapper(queryClient) })

    await waitFor(() => expect(first.result.current.status).toBe('connected'))
    expect(signalRMock.connections).toHaveLength(1)
    expect(signalRMock.withUrl).toHaveBeenCalledWith('/hubs/calendar', expect.objectContaining({ withCredentials: true }))
    expect(signalRMock.withUrl.mock.calls[0][1]).not.toHaveProperty('accessTokenFactory')

    first.unmount()
    second.unmount()
  })

  it('reuses the auth subscription across a StrictMode cleanup and immediate remount', async () => {
    const subscribe = vi.spyOn(useAuthStore, 'subscribe')
    authenticate()
    const queryClient = new QueryClient()
    const first = renderHook(() => useSignalR(), { wrapper: createWrapper(queryClient) })
    first.unmount()
    const remounted = renderHook(() => useSignalR(), { wrapper: createWrapper(queryClient) })

    await waitFor(() => expect(remounted.result.current.status).toBe('connected'))
    expect(subscribe).toHaveBeenCalledTimes(1)

    remounted.unmount()
    await act(async () => { await Promise.resolve() })
    subscribe.mockRestore()
  })

  it('attaches early subscriptions and removes only the same callback', async () => {
    authenticate()
    const queryClient = new QueryClient()
    const { result, unmount } = renderHook(() => useSignalR(), { wrapper: createWrapper(queryClient) })
    const first = vi.fn()
    const second = vi.fn()
    const unsubscribeFirst = result.current.on('EventCreated', first)
    result.current.on('EventCreated', second)

    await waitFor(() => expect(signalRMock.connections).toHaveLength(1))
    const connection = signalRMock.connections[0]
    connection.emit('EventCreated', { id: '1' })
    expect(first).toHaveBeenCalledOnce()
    expect(second).toHaveBeenCalledOnce()

    unsubscribeFirst()
    connection.emit('EventCreated', { id: '2' })
    expect(first).toHaveBeenCalledOnce()
    expect(second).toHaveBeenCalledTimes(2)
    expect(connection.off).toHaveBeenCalledWith('EventCreated', first)
    unmount()
  })

  it('stops a start-in-flight connection when logout clears the session', async () => {
    let releaseStart: (() => void) | undefined
    const startGate = new Promise<void>((resolve) => { releaseStart = resolve })
    signalRMock.setNextStartGate(startGate)
    authenticate()
    const queryClient = new QueryClient()
    const { result, unmount } = renderHook(() => useSignalR(), { wrapper: createWrapper(queryClient) })
    await waitFor(() => expect(signalRMock.connections).toHaveLength(1))
    const connection = signalRMock.connections[0]
    expect(connection.start).toHaveBeenCalledOnce()

    act(() => useAuthStore.getState().logout())
    releaseStart?.()
    await waitFor(() => expect(connection.stop).toHaveBeenCalledOnce())
    expect(result.current.status).toBe('idle')
    await act(async () => { await connection.reconnectedHandler?.() })
    expect(result.current.status).toBe('idle')
    unmount()
  })

  it('manually reconnects the same disconnected connection without duplicating handlers', async () => {
    authenticate()
    const queryClient = new QueryClient()
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue(undefined)
    const { result, unmount } = renderHook(() => useSignalR(), { wrapper: createWrapper(queryClient) })
    await waitFor(() => expect(result.current.status).toBe('connected'))
    expect(invalidate).not.toHaveBeenCalled()
    const connection = signalRMock.connections[0]
    act(() => { connection.state = signalRMock.states.Disconnected; connection.closeHandler?.() })
    expect(result.current.status).toBe('disconnected')

    await act(async () => { await result.current.reconnect() })
    expect(result.current.status).toBe('connected')
    expect(signalRMock.connections).toHaveLength(1)
    expect(connection.start).toHaveBeenCalledTimes(2)
    expect(connection.onreconnected).toHaveBeenCalledOnce()
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['events'] })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['itineraries'] })
    unmount()
  })

  it('keeps subscriptions and resynchronizes queries after reconnection', async () => {
    authenticate()
    const queryClient = new QueryClient()
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue(undefined)
    const { result, unmount } = renderHook(() => useSignalR(), { wrapper: createWrapper(queryClient) })
    const callback = vi.fn()
    result.current.on('EventUpdated', callback)
    await waitFor(() => expect(result.current.status).toBe('connected'))
    const connection = signalRMock.connections[0]

    act(() => connection.reconnectingHandler?.())
    expect(result.current.status).toBe('reconnecting')
    await act(async () => { await connection.reconnectedHandler?.() })
    expect(result.current.status).toBe('connected')
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['events'] })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['itineraries'] })
    connection.emit('EventUpdated')
    expect(callback).toHaveBeenCalledOnce()
    unmount()
  })
})
