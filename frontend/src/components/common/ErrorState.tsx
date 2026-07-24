'use client'

import { CircleAlert } from 'lucide-react'
import { Button } from '@/components/ui/button'

interface ErrorStateProps {
  title: string
  description?: string
  onRetry?: () => void
}

/// 顯示可被輔助科技感知、並能嘗試復原的錯誤狀態。
export default function ErrorState({
  title,
  description,
  onRetry,
}: ErrorStateProps) {
  return (
    <section
      role="alert"
      className="mx-auto flex max-w-xl flex-col items-center gap-4 rounded-2xl border border-destructive/25 bg-destructive/5 px-6 py-10 text-center"
    >
      <span className="grid size-11 place-items-center rounded-full bg-destructive/10 text-destructive">
        <CircleAlert aria-hidden="true" className="size-5" />
      </span>
      <div className="min-w-0 space-y-1.5">
        <h2 className="text-balance text-lg font-semibold text-foreground">{title}</h2>
        {description ? (
          <p className="text-pretty text-sm leading-6 text-muted-foreground">
            {description}
          </p>
        ) : null}
      </div>
      {onRetry ? (
        <Button type="button" variant="outline" className="min-h-11 px-4" onClick={onRetry}>
          重新嘗試
        </Button>
      ) : null}
    </section>
  )
}
