import { Skeleton } from '@/components/ui/skeleton'

/// Portal 路由載入中的穩定佔位版面。
export default function PortalLoading() {
  return (
    <section role="status" aria-label="頁面載入中" aria-busy="true" className="space-y-4">
      <span className="sr-only">頁面載入中</span>
      {Array.from({ length: 3 }, (_, index) => (
        <div key={index} className="space-y-3 rounded-2xl border border-border/70 p-5">
          <Skeleton className="h-5 w-2/5" />
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-4 w-3/4" />
        </div>
      ))}
    </section>
  )
}
