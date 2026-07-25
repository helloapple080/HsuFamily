'use client'

import { useCallback, useEffect, useSyncExternalStore } from 'react'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import * as signalR from '@microsoft/signalr'
import { apiBaseUrl } from '@/lib/api'
import { useAuthStore } from '@/stores/authStore'
import { isDemoMode } from '@/lib/demo/config'

export type SignalRStatus = 'demo' | 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

type EventCallback = (...args: unknown[]) => void
type StatusListener = () => void

function logConnectionFailure(message: string): void {
  // 只記錄固定訊息，不附帶 Cookie、Token、URL 或 server error payload。
  console.error(`[SignalR] ${message}`)
}

class SharedSignalRClient {
  private connection: signalR.HubConnection | null = null
  private readonly eventCallbacks = new Map<string, Set<EventCallback>>()
  private readonly statusListeners = new Set<StatusListener>()
  private readonly queryClients = new Map<QueryClient, number>()
  private status: SignalRStatus = 'idle'
  private references = 0
  private teardownVersion = 0
  private desiredAuthenticated = false
  private unsubscribeAuth: (() => void) | null = null
  private lifecycle: Promise<void> = Promise.resolve()

  readonly getStatus = (): SignalRStatus => this.status

  readonly subscribeStatus = (listener: StatusListener): (() => void) => {
    this.statusListeners.add(listener)
    return () => this.statusListeners.delete(listener)
  }

  acquire(): () => void {
    this.references += 1
    this.teardownVersion += 1

    if (this.references === 1) {
      // StrictMode 的延後 teardown 尚未執行時，沿用既有訂閱，避免覆寫 unsubscribe handle。
      if (!this.unsubscribeAuth) {
        this.unsubscribeAuth = useAuthStore.subscribe((state, previousState) => {
          if (state.isAuthenticated !== previousState.isAuthenticated) {
            this.setAuthenticated(state.isAuthenticated)
          }
        })
      }
      this.setAuthenticated(useAuthStore.getState().isAuthenticated)
    }

    return () => {
      this.references = Math.max(0, this.references - 1)
      const teardownVersion = ++this.teardownVersion

      // StrictMode 會立即 unmount/remount；延後一個 microtask 避免虛假的 stop/start。
      queueMicrotask(() => {
        if (this.references !== 0 || teardownVersion !== this.teardownVersion) return
        this.unsubscribeAuth?.()
        this.unsubscribeAuth = null
        this.setAuthenticated(false)
      })
    }
  }

  registerQueryClient(queryClient: QueryClient): () => void {
    this.queryClients.set(queryClient, (this.queryClients.get(queryClient) ?? 0) + 1)
    return () => {
      const nextCount = (this.queryClients.get(queryClient) ?? 1) - 1
      if (nextCount <= 0) this.queryClients.delete(queryClient)
      else this.queryClients.set(queryClient, nextCount)
    }
  }

  subscribeEvent(event: string, callback: EventCallback): () => void {
    const callbacks = this.eventCallbacks.get(event) ?? new Set<EventCallback>()
    callbacks.add(callback)
    this.eventCallbacks.set(event, callbacks)
    this.connection?.on(event, callback)

    let isSubscribed = true
    return () => {
      if (!isSubscribed) return
      isSubscribed = false
      callbacks.delete(callback)
      if (callbacks.size === 0) this.eventCallbacks.delete(event)
      this.connection?.off(event, callback)
    }
  }

  reconnect = async (): Promise<void> => {
    if (!this.desiredAuthenticated || this.references === 0) return
    await this.enqueueStart(true)
  }

  private setAuthenticated(isAuthenticated: boolean): void {
    this.desiredAuthenticated = isAuthenticated
    if (isAuthenticated && this.references > 0) void this.enqueueStart()
    else void this.enqueueStop()
  }

  private enqueue(task: () => Promise<void>): Promise<void> {
    const queued = this.lifecycle.then(task, task)
    this.lifecycle = queued.catch(() => undefined)
    return queued
  }

  private enqueueStart(resynchronizeAfterStart = false): Promise<void> {
    return this.enqueue(async () => {
      if (!this.desiredAuthenticated || this.references === 0) return

      const connection = this.connection ?? this.buildConnection()
      if (this.connection !== connection) this.connection = connection
      let didStart = false

      if (connection.state === signalR.HubConnectionState.Disconnected) {
        this.setStatus('connecting')
        try {
          await connection.start()
          didStart = true
        } catch {
          if (this.connection === connection && this.desiredAuthenticated) {
            this.setStatus('disconnected')
            logConnectionFailure('即時連線啟動失敗')
          }
          return
        }
      }

      if (this.connection !== connection) return
      if (!this.desiredAuthenticated || this.references === 0) {
        await this.stopAndDiscard(connection)
        return
      }
      this.setStatus('connected')
      if (didStart && resynchronizeAfterStart) await this.resynchronizeQueries()
    })
  }

  private enqueueStop(): Promise<void> {
    return this.enqueue(async () => {
      const connection = this.connection
      if (connection) await this.stopAndDiscard(connection)
      else this.setStatus('idle')
    })
  }

  private async stopAndDiscard(connection: signalR.HubConnection): Promise<void> {
    if (this.connection === connection) this.connection = null
    if (connection.state !== signalR.HubConnectionState.Disconnected) {
      try {
        await connection.stop()
      } catch {
        logConnectionFailure('即時連線停止失敗')
      }
    }
    this.setStatus('idle')
  }

  private buildConnection(): signalR.HubConnection {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/hubs/calendar`, {
        // 專案使用 HttpOnly Cookie；每次連線／重連都由瀏覽器讀取最新 Cookie，前端不持有 access token。
        withCredentials: true,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    this.eventCallbacks.forEach((callbacks, event) => {
      callbacks.forEach((callback) => connection.on(event, callback))
    })

    connection.onreconnecting(() => {
      if (this.connection === connection && this.desiredAuthenticated) this.setStatus('reconnecting')
    })
    connection.onreconnected(async () => {
      if (this.connection !== connection || !this.desiredAuthenticated) return
      this.setStatus('connected')
      await this.resynchronizeQueries()
    })
    connection.onclose(() => {
      if (this.connection !== connection) return
      this.setStatus(this.desiredAuthenticated ? 'disconnected' : 'idle')
    })

    return connection
  }

  private async resynchronizeQueries(): Promise<void> {
    await Promise.all([...this.queryClients.keys()].flatMap((queryClient) => [
      queryClient.invalidateQueries({ queryKey: ['events'] }),
      queryClient.invalidateQueries({ queryKey: ['itineraries'] }),
    ]))
  }

  private setStatus(status: SignalRStatus): void {
    if (this.status === status) return
    this.status = status
    this.statusListeners.forEach((listener) => listener())
  }
}

const sharedSignalRClient = new SharedSignalRClient()
const demoSignalRClient: Pick<SharedSignalRClient, 'getStatus' | 'subscribeStatus' | 'acquire' | 'registerQueryClient' | 'subscribeEvent' | 'reconnect'> = {
  getStatus: (): SignalRStatus => 'demo',
  subscribeStatus: () => () => undefined,
  acquire: (): (() => void) => () => undefined,
  registerQueryClient: () => () => undefined,
  subscribeEvent: () => () => undefined,
  reconnect: async (): Promise<void> => undefined,
}

/// 共用 SignalR 連線與事件訂閱，在重連後同步 React Query cache。
export function useSignalR() {
  const queryClient = useQueryClient()
  const signalRClient = isDemoMode ? demoSignalRClient : sharedSignalRClient
  const status = useSyncExternalStore(
    signalRClient.subscribeStatus,
    signalRClient.getStatus,
    signalRClient.getStatus
  )

  useEffect(() => signalRClient.acquire(), [signalRClient])
  useEffect(() => signalRClient.registerQueryClient(queryClient), [queryClient, signalRClient])

  const on = useCallback((event: string, callback: EventCallback) => (
    signalRClient.subscribeEvent(event, callback)
  ), [signalRClient])

  return { on, status, reconnect: signalRClient.reconnect }
}
