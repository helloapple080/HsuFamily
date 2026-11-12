import HostedCalendar from '@/components/features/calendar/HostedCalendar'
import LegacyCalendar from '@/components/features/calendar/LegacyCalendar'
import { isHostedMode } from '@/lib/hosted/mode'

export default function CalendarPage() {
  return isHostedMode ? <HostedCalendar /> : <LegacyCalendar />
}
