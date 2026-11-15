import './zod-setup'
import { lazy, Suspense } from 'react'
import { createRoot } from 'react-dom/client'
import Loading from '@/app/(portal)/loading'
import { SessionGate } from './session'
import '@/app/globals.css'
import 'sonner/dist/styles.css'
import './spa.css'
import './theme'

const Portal = lazy(() => import('./portal'))

if (window.location.pathname === '/') {
  const { search, hash } = window.location
  window.history.replaceState(null, '', `/calendar${search}${hash}`)
}

createRoot(document.getElementById('root')!).render(
  <SessionGate>{(logout) => (
    <Suspense fallback={<Loading />}><Portal onLogout={logout} /></Suspense>
  )}</SessionGate>,
)
