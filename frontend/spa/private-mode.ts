import type { CalendarEvent, Itinerary } from '@/types/event'
import type { Member } from '@/types/member'

export const isDemoMode = false
export const demoBasePath = ''
export const demoReadOnlyMessage = ''
export const demoEvents: readonly CalendarEvent[] = []
export const demoItineraries: readonly Itinerary[] = []
export const demoMembers: readonly Member[] = []

export async function rejectDemoWrite(): Promise<never> {
  throw new Error('此操作不適用於私人入口網。')
}
