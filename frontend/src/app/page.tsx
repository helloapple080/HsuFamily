import Link from 'next/link'
import { redirect } from 'next/navigation'
import { CalendarDays, MapPinned, Users } from 'lucide-react'
import { isDemoMode } from '@/lib/demo/config'

export default function HomePage() {
  if (!isDemoMode) redirect('/calendar')

  const features = [
    { href: '/calendar', title: '家族行事曆', description: '查看國曆、農曆與祭祖提醒', icon: CalendarDays },
    { href: '/members', title: '家庭成員', description: '瀏覽虛構的成員資料卡', icon: Users },
    { href: '/itineraries', title: '歷年行程', description: '體驗家庭旅遊紀錄與搜尋', icon: MapPinned },
  ]

  return (
    <main className="min-h-screen bg-gradient-mesh px-4 py-16 sm:px-6">
      <div className="mx-auto max-w-5xl space-y-10">
        <section className="space-y-5 text-center">
          <p className="mx-auto w-fit rounded-full border border-amber-500/30 bg-amber-500/10 px-4 py-2 text-sm font-medium">公開展示模式</p>
          <h1 className="text-4xl font-bold tracking-tight sm:text-6xl">許氏入口網</h1>
          <p className="mx-auto max-w-2xl text-lg text-[var(--muted-foreground)]">結合台灣傳統文化與現代工程的家庭入口網站。以下內容皆為虛構資料，不會連接正式資料庫。</p>
        </section>
        <section aria-label="展示功能" className="grid gap-4 md:grid-cols-3">
          {features.map(({ href, title, description, icon: Icon }) => (
            <Link key={href} href={href} className="glass-panel group rounded-2xl p-6 transition-transform hover:-translate-y-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
              <Icon aria-hidden="true" className="mb-5 size-9 text-[var(--primary)]" />
              <h2 className="text-xl font-semibold">{title}</h2>
              <p className="mt-2 text-sm text-[var(--muted-foreground)]">{description}</p>
              <span className="mt-6 inline-block text-sm font-medium text-[var(--primary)]">進入展示 →</span>
            </Link>
          ))}
        </section>
      </div>
    </main>
  )
}
