import PortalLayout from '@/components/layout/PortalLayout'

interface PortalRouteLayoutProps {
  children: React.ReactNode
}

/// 保持 route layout 為 Server Component，互動導覽由專用 client shell 負責。
export default function PortalRouteLayout({ children }: PortalRouteLayoutProps) {
  return <PortalLayout>{children}</PortalLayout>
}
