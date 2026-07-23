---
name: signalr-realtime
description: >-
  SignalR 即時通訊開發模式。包含後端 Hub 設定與前端 Client 連線管理。
  Use when implementing real-time features, event broadcasting, or live updates.
---

# SignalR 即時通訊技能

## 後端 Hub 擴展

### 新增 Hub 方法
檔案: `backend/FamilyPortal.Infrastructure/Hubs/CalendarHub.cs`

```csharp
public class CalendarHub : Hub
{
    // Hub URL 路由
    public const string HubUrl = "/hubs/calendar";

    // Client 可呼叫的方法
    public async Task JoinEventRoom(string eventId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"event-{eventId}");
    }
}
```

### Controller 中推播
```csharp
// 注入 IHubContext
private readonly IHubContext<CalendarHub> _calendarHub;

// 推播給所有用戶
await _calendarHub.Clients.Group("FamilyCalendar")
    .SendAsync("EventCreated", eventResponse);

// 推播給特定群組
await _calendarHub.Clients.Group($"event-{eventId}")
    .SendAsync("EventUpdated", eventResponse);
```

## 前端 SignalR Client

### 連線管理 Hook
```typescript
'use client'

import { useEffect, useRef, useCallback } from 'react'
import * as signalR from '@microsoft/signalr'
import { useAuthStore } from '@/stores/authStore'

export function useSignalR() {
  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const { accessToken } = useAuthStore()

  useEffect(() => {
    if (!accessToken) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${process.env.NEXT_PUBLIC_API_URL}/hubs/calendar`, {
        accessTokenFactory: () => accessToken,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000]) // 重連策略
      .configureLogging(signalR.LogLevel.Information)
      .build()

    connectionRef.current = connection

    connection.start()
      .catch(err => console.error('SignalR 連線失敗:', err))

    return () => {
      connection.stop()
    }
  }, [accessToken])

  // 訂閱事件
  const on = useCallback((event: string, callback: (...args: unknown[]) => void) => {
    connectionRef.current?.on(event, callback)
    return () => connectionRef.current?.off(event, callback)
  }, [])

  return { connection: connectionRef.current, on }
}
```

### 在元件中使用
```typescript
'use client'

import { useEffect } from 'react'
import { useSignalR } from '@/hooks/useSignalR'
import { useQueryClient } from '@tanstack/react-query'

export default function CalendarLiveSync() {
  const { on } = useSignalR()
  const queryClient = useQueryClient()

  useEffect(() => {
    // 事件新增 → 自動更新 React Query Cache
    const unsub1 = on('EventCreated', () => {
      queryClient.invalidateQueries({ queryKey: ['events'] })
    })

    // 事件更新
    const unsub2 = on('EventUpdated', () => {
      queryClient.invalidateQueries({ queryKey: ['events'] })
    })

    // 事件刪除
    const unsub3 = on('EventDeleted', () => {
      queryClient.invalidateQueries({ queryKey: ['events'] })
    })

    return () => { unsub1?.(); unsub2?.(); unsub3?.() }
  }, [on, queryClient])

  return null // 純邏輯元件
}
```

## SignalR 事件命名規範
| 事件名 | 方向 | 說明 |
|--------|------|------|
| `EventCreated` | Server → Client | 新事件建立 |
| `EventUpdated` | Server → Client | 事件更新 |
| `EventDeleted` | Server → Client | 事件刪除 |

## 連線狀態處理
```typescript
connection.onreconnecting(() => {
  console.log('SignalR 重連中...')
  // 顯示 UI 提示
})

connection.onreconnected(() => {
  console.log('SignalR 重連成功')
  // 重新載入最新資料
  queryClient.invalidateQueries({ queryKey: ['events'] })
})

connection.onclose(() => {
  console.log('SignalR 連線關閉')
  // 顯示離線提示
})
```
