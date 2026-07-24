'use client'

import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { endOfMonth, endOfWeek, format, startOfMonth, startOfWeek } from 'date-fns'
import { CalendarDays, ChevronLeft, ChevronRight, Plus } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import AdminOnly from '@/components/common/AdminOnly'
import { EventDialog } from '@/components/features/calendar/EventDialog'
import { useCalendarEvents } from '@/hooks/useCalendarEvents'
import { useSignalR } from '@/hooks/useSignalR'
import { getMonthLunarInfo } from '@/lib/lunar'
import { useAuthStore } from '@/stores/authStore'

const monthNames = ['一月', '二月', '三月', '四月', '五月', '六月', '七月', '八月', '九月', '十月', '十一月', '十二月']
const dayNames = ['日', '一', '二', '三', '四', '五', '六']

export default function CalendarPage() {
  const [currentDate, setCurrentDate] = useState(new Date())
  const [selectedDate, setSelectedDate] = useState<string | null>(null)
  const isAdmin = useAuthStore((state) => state.isAdmin)
  const year = currentDate.getFullYear()
  const month = currentDate.getMonth() + 1
  const lunarDays = getMonthLunarInfo(year, month)
  const emptyDays = Array.from({ length: new Date(year, month - 1, 1).getDay() })
  const queryStart = format(startOfWeek(startOfMonth(currentDate)), 'yyyy-MM-dd')
  const queryEnd = format(endOfWeek(endOfMonth(currentDate)), 'yyyy-MM-dd')
  const { data: events = [], isLoading } = useCalendarEvents(queryStart, queryEnd)
  const { on } = useSignalR()
  const queryClient = useQueryClient()

  useEffect(() => {
    const invalidate = () => queryClient.invalidateQueries({ queryKey: ['events'] })
    const unsubscribe = [
      on('EventCreated', invalidate),
      on('EventUpdated', invalidate),
      on('EventDeleted', invalidate),
    ]
    return () => unsubscribe.forEach((callback) => callback?.())
  }, [on, queryClient])

  return (
    <div className="space-y-6">
      <header className="flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold tracking-tight">
            <CalendarDays aria-hidden="true" className="size-6" />
            行事曆
          </h1>
          <p className="mt-1 text-sm text-[var(--muted-foreground)]">
            {year} 年 {monthNames[month - 1]} — 國農曆雙顯示
          </p>
        </div>
        <div className="flex flex-wrap gap-2" aria-label="行事曆圖例">
          <Badge variant="outline" className="text-xs">農曆日期</Badge>
          <Badge variant="outline" className="border-[var(--festival)] text-xs text-[var(--festival)]">節日</Badge>
          <Badge variant="outline" className="border-[var(--worship)] text-xs text-[var(--worship)]">拜拜日</Badge>
        </div>
      </header>

      <Card className="glass-panel border-0">
        <CardHeader className="pb-4">
          <CardTitle className="flex flex-col justify-between gap-3 text-lg sm:flex-row sm:items-center">
            <span>{year} 年 {monthNames[month - 1]}</span>
            <div className="flex flex-wrap items-center gap-2">
              <Button type="button" variant="ghost" size="icon" className="size-11" aria-label="上個月" onClick={() => setCurrentDate(new Date(year, month - 2, 1))}>
                <ChevronLeft aria-hidden="true" />
              </Button>
              <Button type="button" variant="secondary" className="h-11 px-4" onClick={() => setCurrentDate(new Date())}>今天</Button>
              <Button type="button" variant="ghost" size="icon" className="size-11" aria-label="下個月" onClick={() => setCurrentDate(new Date(year, month, 1))}>
                <ChevronRight aria-hidden="true" />
              </Button>
              <AdminOnly>
                <EventDialog defaultDate={new Date()}>
                  <Button className="h-11 gap-2 glass-button" aria-label="新增事件">
                    <Plus aria-hidden="true" />
                    新增
                  </Button>
                </EventDialog>
              </AdminOnly>
            </div>
          </CardTitle>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="flex h-[400px] items-center justify-center" role="status">載入行事曆中…</div>
          ) : (
            <>
              <div className="overflow-x-auto overscroll-x-contain pb-2">
                <div className="mb-2 grid min-w-[21rem] grid-cols-7 gap-1" aria-hidden="true">
                  {dayNames.map((day) => <div key={day} className="py-2 text-center text-xs font-medium text-[var(--muted-foreground)]">{day}</div>)}
                </div>
                <div className="grid min-w-[21rem] grid-cols-7 gap-1" role="grid" aria-label={`${year} 年 ${monthNames[month - 1]}`}>
                {emptyDays.map((_, index) => <div key={`empty-${index}`} role="gridcell" />)}
                {lunarDays.map((dayInfo) => {
                  const isToday = dayInfo.solarDate === format(new Date(), 'yyyy-MM-dd')
                  const isSelected = selectedDate === dayInfo.solarDate
                  const dayEvents = events.filter((event) => event.startDate.startsWith(dayInfo.solarDate))
                  const hasFestival = dayInfo.festivals.length > 0 || dayInfo.solarFestivals.length > 0
                  const isWorship = dayInfo.lunarDay === '初一' || dayInfo.lunarDay === '十五'
                  const accessibleLunarDate = `農曆${dayInfo.isLeapMonth ? '閏' : ''}${dayInfo.lunarMonth}月${dayInfo.lunarDay}`
                  const accessibleDate = `${format(new Date(`${dayInfo.solarDate}T00:00:00`), 'yyyy年M月d日')}，${accessibleLunarDate}`
                  const dayButton = (
                    <button
                      type="button"
                      aria-label={accessibleDate}
                      aria-pressed={!isAdmin ? isSelected : undefined}
                      onClick={!isAdmin ? () => setSelectedDate(dayInfo.solarDate) : undefined}
                      className={`min-h-16 min-w-11 w-full rounded-lg p-2 text-left outline-none transition-colors hover:bg-[var(--accent)] focus-visible:ring-2 focus-visible:ring-ring ${
                        isToday ? 'bg-[var(--primary)]/10 ring-1 ring-[var(--primary)]' : ''
                      } ${isSelected ? 'bg-[var(--accent)]' : ''}`}
                    >
                      <span className="flex items-start justify-between gap-1">
                        <span className={`text-sm font-medium ${isToday ? 'text-[var(--primary)]' : ''}`}>{Number(dayInfo.solarDate.slice(-2))}</span>
                        <span className={`lunar-text text-right ${hasFestival ? 'font-bold text-[var(--festival)]' : ''} ${isWorship && !hasFestival ? 'font-bold text-[var(--worship)]' : ''}`}>
                          {hasFestival ? (dayInfo.festivals[0] || dayInfo.solarFestivals[0]) : isWorship ? '拜拜' : dayInfo.displayText}
                        </span>
                      </span>
                    </button>
                  )

                  return (
                    <article key={dayInfo.solarDate} role="gridcell" className="min-h-24 min-w-0 rounded-lg">
                      {isAdmin ? <EventDialog defaultDate={new Date(`${dayInfo.solarDate}T00:00:00`)}>{dayButton}</EventDialog> : dayButton}
                      <div className="mt-1 space-y-1">
                        {dayEvents.slice(0, 2).map((event) => isAdmin ? (
                          <EventDialog key={event.id} event={event}>
                            <button type="button" className="min-h-11 w-full truncate rounded-md bg-[var(--primary)] px-1.5 text-left text-[10px] text-[var(--primary-foreground)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring" aria-label={`編輯事件：${event.title}`}>
                              {event.title}
                            </button>
                          </EventDialog>
                        ) : (
                          <p key={event.id} className="truncate rounded bg-[var(--primary)] px-1.5 py-1 text-[10px] text-[var(--primary-foreground)]">{event.title}</p>
                        ))}
                        {dayEvents.length > 2 && <p className="px-1 text-[10px] text-[var(--muted-foreground)]">+{dayEvents.length - 2} 多項</p>}
                      </div>
                    </article>
                  )
                })}
                </div>
              </div>
            </>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
