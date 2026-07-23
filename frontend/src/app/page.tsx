import { redirect } from 'next/navigation'

/// 首頁 — 直接重導向到行事曆頁面
export default function HomePage() {
  redirect('/calendar')
}
