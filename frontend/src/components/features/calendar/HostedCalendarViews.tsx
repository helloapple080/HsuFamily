'use client'

import { useState } from 'react'
import { Button } from '@/components/ui/button'
import HostedCalendar from './HostedCalendar'
import ActivityCalendar from './ActivityCalendar'

export default function HostedCalendarViews() {
  const [view, setView] = useState<'worship' | 'month'>('worship')
  return (
    <div className="space-y-6">
      <div role="group" aria-label="行事曆檢視" className="grid w-full grid-cols-2 gap-1 rounded-xl border border-border bg-muted p-1 sm:w-fit">
        <Button type="button" className="h-auto min-h-11 min-w-0 px-4 whitespace-normal" variant={view === 'worship' ? 'default' : 'ghost'}
          aria-pressed={view === 'worship'} onClick={() => setView('worship')}>拜拜日期</Button>
        <Button type="button" className="h-auto min-h-11 min-w-0 px-4 whitespace-normal" variant={view === 'month' ? 'default' : 'ghost'}
          aria-pressed={view === 'month'} onClick={() => setView('month')}>活動月曆</Button>
      </div>
      {view === 'worship' ? <HostedCalendar /> : <ActivityCalendar />}
    </div>
  )
}
