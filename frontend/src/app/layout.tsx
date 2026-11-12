import type { Metadata } from 'next'
import { connection } from 'next/server'
import Providers from '@/components/layout/Providers'
import { isDemoMode } from '@/lib/demo/config'
import './globals.css'
import 'sonner/dist/styles.css'

export const metadata: Metadata = {
  title: {
    default: '許氏入口網 (Hsu Family Portal)',
    template: '%s | 許氏入口網',
  },
  description: '私人家庭入口網，整合農曆行事曆、拜拜日期、歷年行程與私人照片。',
  keywords: ['家庭', '行事曆', '農曆', '拜拜'],
}

export default async function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
  // 正式版需逐 request 產生 CSP nonce；靜態展示版沒有 request context。
  if (!isDemoMode) await connection()

  return (
    <html
      lang="zh-TW"
      className="h-full"
      suppressHydrationWarning
    >
      <body className="min-h-full flex flex-col antialiased">
        <Providers>{children}</Providers>
      </body>
    </html>
  )
}
