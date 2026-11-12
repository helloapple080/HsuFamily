import HostedCalendarViews from '@/components/features/calendar/HostedCalendarViews'
import LegacyCalendar from '@/components/features/calendar/LegacyCalendar'
import { isHostedMode } from '@/lib/hosted/mode'

export default function CalendarPage() {
  return isHostedMode ? <HostedCalendarViews /> : <LegacyCalendar />
}
