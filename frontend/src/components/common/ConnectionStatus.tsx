'use client'

import { useState } from 'react'
import { LoaderCircle, WifiOff } from 'lucide-react'
import { Button } from '@/components/ui/button'
import type { SignalRStatus } from '@/hooks/useSignalR'

interface ConnectionStatusProps {
  status: SignalRStatus
  reconnect: () => Promise<void>
}

/// 以非侵入式提示即時連線狀態，並在中斷時提供手動復原。
export default function ConnectionStatus({ status, reconnect }: ConnectionStatusProps) {
  const [isRetrying, setIsRetrying] = useState(false)

  if (status === 'demo') {
    return <p className="text-xs text-[var(--muted-foreground)]">即時狀態：展示資料</p>
  }

  if (status === 'idle' || status === 'connected') return null

  if (status === 'connecting' || status === 'reconnecting') {
    return (
      <div role="status" className="flex min-h-11 items-center gap-2 rounded-lg bg-[var(--secondary)] px-3 text-sm text-[var(--muted-foreground)]">
        <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
        {status === 'reconnecting' ? '即時更新重新連線中' : '即時更新連線中'}
      </div>
    )
  }

  const handleReconnect = async () => {
    setIsRetrying(true)
    try {
      await reconnect()
    } finally {
      setIsRetrying(false)
    }
  }

  return (
    <div role="status" className="flex flex-col items-start justify-between gap-2 rounded-lg bg-[var(--secondary)] px-3 py-2 text-sm sm:flex-row sm:items-center">
      <span className="flex items-center gap-2 text-[var(--muted-foreground)]">
        <WifiOff aria-hidden="true" className="size-4" />
        即時更新已中斷
      </span>
      <Button type="button" variant="outline" className="h-11" disabled={isRetrying} onClick={handleReconnect}>
        {isRetrying ? '重新連線中…' : '重新連線'}
      </Button>
    </div>
  )
}
