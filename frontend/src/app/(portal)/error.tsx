'use client'

import ErrorState from '@/components/common/ErrorState'

interface PortalErrorProps {
  error: Error & { digest?: string }
  reset: () => void
}

/// Portal 路由發生錯誤時提供就地復原。
export default function PortalError({ reset }: PortalErrorProps) {
  return (
    <ErrorState
      title="頁面暫時無法載入"
      description="請檢查網路連線後再試一次。"
      onRetry={reset}
    />
  )
}
