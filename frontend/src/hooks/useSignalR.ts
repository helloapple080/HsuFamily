'use client'

import { useEffect, useRef, useCallback } from 'react'
import * as signalR from '@microsoft/signalr'
import { useAuthStore } from '@/stores/authStore'

export function useSignalR() {
  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const { isAuthenticated } = useAuthStore()

  useEffect(() => {
    if (!isAuthenticated) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5001'}/hubs/calendar`, {
        withCredentials: true,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000]) // 重連策略
      .configureLogging(signalR.LogLevel.Information)
      .build()

    connectionRef.current = connection

    connection.start()
      .then(() => {
        console.log('SignalR connected')
      })
      .catch(err => console.error('SignalR 連線失敗:', err))

    connection.onreconnecting(() => {
      console.log('SignalR 重連中...')
    })

    connection.onreconnected(() => {
      console.log('SignalR 重連成功')
    })

    connection.onclose(() => {
      console.log('SignalR 連線關閉')
    })

    return () => {
      connection.stop()
    }
  }, [isAuthenticated])

  // 訂閱事件
  const on = useCallback((event: string, callback: (...args: unknown[]) => void) => {
    if (connectionRef.current) {
      connectionRef.current.on(event, callback)
    }
    return () => {
      if (connectionRef.current) {
        connectionRef.current.off(event, callback)
      }
    }
  }, [])

  return { on }
}
