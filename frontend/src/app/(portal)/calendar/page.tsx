'use client'

import { useState, useEffect } from 'react'
import { motion } from 'framer-motion'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { getMonthLunarInfo } from '@/lib/lunar'
import { useCalendarEvents } from '@/hooks/useCalendarEvents'
import { useSignalR } from '@/hooks/useSignalR'
import { useQueryClient } from '@tanstack/react-query'
import { startOfMonth, endOfMonth, endOfWeek, startOfWeek, format } from 'date-fns'
import { EventDialog } from '@/components/features/calendar/EventDialog'
import { Button } from '@/components/ui/button'

export default function CalendarPage() {
  const [currentDate, setCurrentDate] = useState(new Date())
  const year = currentDate.getFullYear()
  const month = currentDate.getMonth() + 1 // 1-12
  
  const monthNames = ['一月', '二月', '三月', '四月', '五月', '六月', '七月', '八月', '九月', '十月', '十一月', '十二月']
  const dayNames = ['日', '一', '二', '三', '四', '五', '六']

  // 取得整月資訊
  const lunarDays = getMonthLunarInfo(year, month)
  
  // 計算第一天是星期幾
  const firstDayOfWeek = new Date(year, month - 1, 1).getDay()
  const emptyDays = Array.from({ length: firstDayOfWeek }).map(() => null)

  // API 取得事件
  const queryStart = format(startOfWeek(startOfMonth(currentDate)), 'yyyy-MM-dd')
  const queryEnd = format(endOfWeek(endOfMonth(currentDate)), 'yyyy-MM-dd')
  
  const { data: events = [], isLoading } = useCalendarEvents(queryStart, queryEnd)

  // SignalR 即時同步
  const { on } = useSignalR()
  const queryClient = useQueryClient()

  useEffect(() => {
    const unsub1 = on('EventCreated', () => queryClient.invalidateQueries({ queryKey: ['events'] }))
    const unsub2 = on('EventUpdated', () => queryClient.invalidateQueries({ queryKey: ['events'] }))
    const unsub3 = on('EventDeleted', () => queryClient.invalidateQueries({ queryKey: ['events'] }))

    return () => { unsub1?.(); unsub2?.(); unsub3?.() }
  }, [on, queryClient])

  const goNextMonth = () => setCurrentDate(new Date(year, month, 1))
  const goPrevMonth = () => setCurrentDate(new Date(year, month - 2, 1))
  const goToday = () => setCurrentDate(new Date())

  return (
    <div className="space-y-6">
      <motion.div
        initial={{ opacity: 0, y: -10 }}
        animate={{ opacity: 1, y: 0 }}
        className="flex items-center justify-between"
      >
        <div>
          <h1 className="text-2xl font-bold tracking-tight">📅 行事曆</h1>
          <p className="text-sm text-[var(--muted-foreground)] mt-1">
            {year} 年 {monthNames[month - 1]} — 國農曆雙顯示
          </p>
        </div>
        <div className="flex gap-2">
          <Badge variant="outline" className="text-xs">
            🎋 農曆日期
          </Badge>
          <Badge variant="outline" className="text-xs border-[var(--festival)] text-[var(--festival)]">
            🏮 節日
          </Badge>
          <Badge variant="outline" className="text-xs border-[var(--worship)] text-[var(--worship)]">
            🙏 拜拜日
          </Badge>
        </div>
      </motion.div>

      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.1 }}
      >
        <Card className="glass-panel border-0">
          <CardHeader className="pb-4">
            <CardTitle className="text-lg flex items-center justify-between">
              <span>{year} 年 {monthNames[month - 1]}</span>
              <div className="flex gap-2">
                <button onClick={goPrevMonth} className="p-2 rounded-lg hover:bg-[var(--accent)] transition-colors text-sm">
                  ◀
                </button>
                <button onClick={goToday} className="px-3 py-1 rounded-lg bg-[var(--primary)] text-[var(--primary-foreground)] text-sm">
                  今天
                </button>
                <button onClick={goNextMonth} className="p-2 rounded-lg hover:bg-[var(--accent)] transition-colors text-sm">
                  ▶
                </button>
                <div className="w-px h-6 bg-[var(--border)] mx-1" />
                <EventDialog defaultDate={new Date()}>
                  <Button size="sm" className="glass-button text-xs gap-1 h-8">
                    + 新增
                  </Button>
                </EventDialog>
              </div>
            </CardTitle>
          </CardHeader>
          <CardContent>
            {isLoading ? (
               <div className="flex justify-center items-center h-[400px]">載入中...</div>
            ) : (
               <>
                <div className="grid grid-cols-7 gap-1 mb-2">
                  {dayNames.map((day) => (
                    <div key={day} className="text-center text-xs font-medium text-[var(--muted-foreground)] py-2">
                      {day}
                    </div>
                  ))}
                </div>

                <div className="grid grid-cols-7 gap-1">
                  {emptyDays.map((_, i) => <div key={`empty-${i}`} className="min-h-[80px]" />)}
                  
                  {lunarDays.map((dayInfo, i) => {
                    const isToday = dayInfo.solarDate === format(new Date(), 'yyyy-MM-dd')
                    const dayEvents = events.filter(e => e.startDate.startsWith(dayInfo.solarDate))
                    const hasFestival = dayInfo.festivals.length > 0 || dayInfo.solarFestivals.length > 0
                    const isWorship = dayInfo.lunarDay === '初一' || dayInfo.lunarDay === '十五'

                    return (
                      <EventDialog key={i} defaultDate={new Date(dayInfo.solarDate)}>
                        <motion.div
                          initial={{ opacity: 0, scale: 0.9 }}
                          animate={{ opacity: 1, scale: 1 }}
                          transition={{ delay: i * 0.01 }}
                          className={`relative min-h-[90px] rounded-lg p-2 transition-all duration-200 cursor-pointer hover:bg-[var(--accent)] ${
                            isToday ? 'bg-[var(--primary)]/10 ring-1 ring-[var(--primary)] shadow-sm' : ''
                          }`}
                        >
                          <div className="flex justify-between items-start">
                            <span className={`text-sm font-medium ${isToday ? 'text-[var(--primary)]' : ''}`}>
                              {dayInfo.solarDate.split('-')[2]}
                            </span>
                            <span className={`lunar-text text-right ${hasFestival ? 'text-[var(--festival)] font-bold' : ''} ${isWorship && !hasFestival ? 'text-[var(--worship)] font-bold' : ''}`}>
                              {hasFestival ? (dayInfo.festivals[0] || dayInfo.solarFestivals[0]) :
                               isWorship ? '拜拜' :
                               dayInfo.displayText}
                            </span>
                          </div>
                          
                          <div className="mt-1 flex flex-col gap-1 inline-block w-full">
                             {dayEvents.slice(0, 2).map(e => (
                               <div key={e.id} onClick={(ev) => ev.stopPropagation()}>
                                 <EventDialog event={e}>
                                   <div className="text-[10px] truncate rounded bg-gradient-to-r from-[var(--primary)] to-[oklch(0.6_0.18_260)] text-white px-1.5 py-0.5 shadow-sm hover:brightness-110 cursor-pointer">
                                     {e.title}
                                   </div>
                                 </EventDialog>
                               </div>
                             ))}
                             {dayEvents.length > 2 && (
                               <div className="text-[10px] text-[var(--muted-foreground)] px-1">
                                 +{dayEvents.length - 2} 多項
                               </div>
                             )}
                          </div>
                        </motion.div>
                      </EventDialog>
                    )
                  })}
                </div>
               </>
            )}
          </CardContent>
        </Card>
      </motion.div>
    </div>
  )
}
