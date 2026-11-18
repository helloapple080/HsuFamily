import { useSyncExternalStore, type ComponentProps } from 'react'

const routePaths = ['/calendar', '/members', '/itineraries', '/household'] as const
type RoutePath = (typeof routePaths)[number]
const navigationEvent = 'portal:navigate'

export function getPortalRoute(pathname: string): RoutePath | undefined {
  const normalized = pathname.replace(/\/+$/, '') || '/'
  if (normalized === '/') return '/calendar'
  return routePaths.find((path) => path === normalized)
}

function subscribe(listener: () => void) {
  window.addEventListener('popstate', listener)
  window.addEventListener(navigationEvent, listener)
  return () => {
    window.removeEventListener('popstate', listener)
    window.removeEventListener(navigationEvent, listener)
  }
}

function getPathname() {
  return window.location.pathname
}

export function usePathname() {
  return useSyncExternalStore(subscribe, getPathname)
}

function navigate(href: string, replace = false) {
  if (href === getPathname()) return
  window.history[replace ? 'replaceState' : 'pushState'](null, '', href)
  window.dispatchEvent(new Event(navigationEvent))
  document.getElementById('main-content')?.focus({ preventScroll: true })
  window.scrollTo(0, 0)
}

const router = {
  push: (href: string) => navigate(href),
  replace: (href: string) => navigate(href, true),
}

export function useRouter() {
  return router
}

/// 只攔截站內頁面；保留瀏覽器的新分頁、輔助鍵和外部連結行為。
export default function Link({ href, onClick, target, download, ...props }: Omit<ComponentProps<'a'>, 'href'> & { href: string }) {
  return <a {...props} href={href} target={target} download={download} onClick={(event) => {
    onClick?.(event)
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey
      || (target && target !== '_self') || download !== undefined || !routePaths.some((path) => path === href)) return
    event.preventDefault()
    navigate(href)
  }} />
}
