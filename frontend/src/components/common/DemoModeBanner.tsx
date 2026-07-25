import { Eye } from 'lucide-react'

export default function DemoModeBanner() {
  return (
    <div role="status" className="flex min-h-11 flex-wrap items-center gap-x-2 gap-y-1 rounded-xl border border-amber-500/30 bg-amber-500/10 px-4 py-2 text-sm">
      <Eye aria-hidden="true" className="size-4 shrink-0 text-amber-600 dark:text-amber-400" />
      <strong>公開展示模式</strong>
      <span className="text-[var(--muted-foreground)]">資料皆為虛構內容，操作不會儲存。</span>
    </div>
  )
}
