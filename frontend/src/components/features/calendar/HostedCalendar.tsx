'use client'

import { useMemo, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useWorshipRules } from '@/hooks/useWorshipRules'
import { formatLunarDate, solarToLunar } from '@/lib/lunar'
import { getWorshipDates } from '@/lib/hosted/worship'
import type { WorshipInput, WorshipRule } from '@/lib/hosted/worship-contract'
import { useAuthStore } from '@/stores/authStore'
import WorshipEditor from './WorshipEditor'
import PrivatePhotos from '@/components/features/media/PrivatePhotos'
import { isAxiosError } from 'axios'

const emptyRule: WorshipInput = { title: '', lunarMonth: 1, lunarDay: 1, leapMonthPolicy: 'RegularOnly', notes: '' }
const emptyRules: WorshipRule[] = []
const yearFormat = new Intl.DateTimeFormat('en', { timeZone: 'Asia/Taipei', year: 'numeric' })

function lunarLabel(date: string) {
  const [year, month, day] = date.split('-').map(Number)
  const lunar = solarToLunar(year, month, day)
  return formatLunarDate(lunar.month, lunar.day, lunar.isLeapMonth)
}

export default function HostedCalendar() {
  const [yearText, setYearText] = useState(() => yearFormat.format(new Date()))
  const [editor, setEditor] = useState<WorshipRule | 'new' | null>(null)
  const isAdmin = useAuthStore((state) => state.isAdmin)
  const { rules, save, remove } = useWorshipRules()
  const year = Number(yearText)
  const validYear = Number.isInteger(year) && year >= 1901 && year <= 2100
  const entries = rules.data ?? emptyRules
  const dates = useMemo(() => !validYear ? [] : entries
    .flatMap((rule) => getWorshipDates(rule, year).map((date) => ({ date, rule })))
    .sort((a, b) => a.date.localeCompare(b.date)), [entries, validYear, year])

  return (
    <section className="space-y-5">
      <header>
        <h1 className="text-2xl font-bold">農曆拜拜行事曆</h1>
        <p className="text-sm text-muted-foreground">依你選的國曆年份換算家族拜拜日期。</p>
      </header>
      <label className="grid max-w-40 gap-1 text-sm">國曆年份
        <Input type="number" min={1901} max={2100} value={yearText} onChange={(event) => setYearText(event.target.value)} aria-invalid={!validYear} />
      </label>
      {!validYear && <p role="alert">請輸入 1901 至 2100 年。</p>}
      {rules.isPending && <p role="status">載入行事曆中…</p>}
      {rules.isError && <div role="alert"><p>暫時無法讀取拜拜規則。</p><Button variant="outline" onClick={() => void rules.refetch()}>重試</Button></div>}
      {validYear && rules.isSuccess && (
        dates.length === 0 ? <p>這一年沒有符合規則的拜拜日期。</p> : (
          <ul className="space-y-2">
            {dates.map(({ date, rule }) => (
              <li key={`${rule.id}-${date}`} className="rounded-lg border border-border p-3">
                <p className="font-medium">{rule.title}</p>
                <time dateTime={date}>{date}</time>
                <span className="ml-2 text-sm text-muted-foreground">{lunarLabel(date)}</span>
                {rule.notes && <p className="text-sm">{rule.notes}</p>}
              </li>
            ))}
          </ul>
        )
      )}
      {(isAdmin || (rules.isSuccess && entries.length > 0)) && (
        <section aria-label="拜拜規則與照片" className="space-y-3 border-t border-border pt-4">
          <h2 className="text-lg font-semibold">拜拜規則與照片</h2>
          {isAdmin && <Button type="button" onClick={() => setEditor('new')}>新增規則</Button>}
          {isAdmin && editor && <WorshipEditor key={editor === 'new' ? 'new' : editor.id}
            initial={editor === 'new' ? emptyRule : editor} onCancel={() => setEditor(null)}
            onSave={async (input) => {
              await save.mutateAsync({ input, id: editor === 'new' ? undefined : editor.id })
              setEditor((current) => current === editor ? null : current)
            }} />}
          {remove.isError && <p role="alert">{isAxiosError(remove.error) && remove.error.response?.status === 409
            ? '請先移除這筆規則的照片，再刪除規則。' : '刪除未完成，請稍後再試。'}</p>}
          <ul className="space-y-2">
            {entries.map((rule) => <li key={rule.id} className="space-y-2 rounded-md border border-border p-3">
              <div className="flex flex-wrap items-center gap-2">
                <span className="mr-auto break-words font-medium">{rule.title}</span>
                {isAdmin && <>
                  <Button type="button" variant="outline" aria-label={`編輯 ${rule.title}`} onClick={() => setEditor(rule)}>編輯</Button>
                  <Button type="button" variant="destructive" aria-label={`刪除 ${rule.title}`} disabled={remove.isPending}
                    onClick={() => { if (window.confirm(`確定刪除「${rule.title}」？`)) remove.mutate(rule.id) }}>刪除</Button>
                </>}
              </div>
              <PrivatePhotos kind="worship" id={rule.id} title={rule.title} />
            </li>)}
          </ul>
        </section>
      )}
    </section>
  )
}
