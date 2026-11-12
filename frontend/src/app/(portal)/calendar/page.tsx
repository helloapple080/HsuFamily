import HostedCalendarViews from '@/components/features/calendar/HostedCalendarViews'
import ActivityCalendar from '@/components/features/calendar/ActivityCalendar'
import { isHostedMode } from '@/lib/hosted/mode'

export default function CalendarPage() {
  return isHostedMode ? <HostedCalendarViews /> : <ActivityCalendar />
}
