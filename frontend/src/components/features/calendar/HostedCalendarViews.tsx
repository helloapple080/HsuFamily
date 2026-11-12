'use client'

import { useState } from 'react'
import { Button } from '@/components/ui/button'
import HostedCalendar from './HostedCalendar'
import ActivityCalendar from './ActivityCalendar'

export default function HostedCalendarViews() {
  const [view, setView] = useState<'worship' | 'month'>('worship')
  return (
    <div className="space-y-5">
      <div role="group" aria-label="行事曆檢視" className="flex flex-wrap gap-2">
        <Button type="button" className="h-11" variant={view === 'worship' ? 'default' : 'outline'}
          aria-pressed={view === 'worship'} onClick={() => setView('worship')}>拜拜日期</Button>
        <Button type="button" className="h-11" variant={view === 'month' ? 'default' : 'outline'}
          aria-pressed={view === 'month'} onClick={() => setView('month')}>活動月曆</Button>
      </div>
      {view === 'worship' ? <HostedCalendar /> : <ActivityCalendar />}
    </div>
  )
}
