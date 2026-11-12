import { isDemoMode } from '@/lib/demo/config'

export const isHostedMode = !isDemoMode && process.env.NEXT_PUBLIC_PORTAL_MODE === 'supabase'
