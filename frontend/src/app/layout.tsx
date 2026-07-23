import type { Metadata } from 'next'
import Providers from '@/components/layout/Providers'
import './globals.css'

export const metadata: Metadata = {
  title: {
    default: '許氏入口網 (Hsu Family Portal)',
    template: '%s | 許氏入口網',
  },
  description: '企業級家庭專屬入口網站，整合農曆行事曆、拜拜提醒、即時同步更新。',
  keywords: ['家庭', '行事曆', '農曆', '拜拜', '即時同步'],
}

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
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
