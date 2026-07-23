import type { Metadata } from 'next'

export const metadata: Metadata = {
  title: '登入',
  description: '登入 Family Portal 家庭入口網站',
}

export default function AuthLayout({
  children,
}: {
  children: React.ReactNode
}) {
  return <>{children}</>
}
