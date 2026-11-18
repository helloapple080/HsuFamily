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
import { isVersionConflict, versionConflictMessage } from '@/lib/recordVersion'

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
    <section className="max-w-4xl space-y-5">
      <div className="flex flex-wrap items-end justify-between gap-x-8 gap-y-4">
        <header className="space-y-1.5">
          <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">農曆拜拜行事曆</h1>
          <p className="text-sm text-muted-foreground">依你選的國曆年份換算家族拜拜日期。</p>
        </header>
        <label className="grid w-full max-w-36 gap-1.5 text-sm font-medium">國曆年份
          <Input type="number" min={1901} max={2100} value={yearText} onChange={(event) => setYearText(event.target.value)} aria-invalid={!validYear} className="h-11 tabular-nums" />
        </label>
      </div>
      {!validYear && <p role="alert">請輸入 1901 至 2100 年。</p>}
      {rules.isPending && <p role="status">載入行事曆中…</p>}
      {rules.isError && <div role="alert"><p>暫時無法讀取拜拜規則。</p><Button variant="outline" onClick={() => void rules.refetch()}>重試</Button></div>}
      {validYear && rules.isSuccess && (
        dates.length === 0 ? <p>這一年沒有符合規則的拜拜日期。</p> : (
          <ul className="space-y-2">
            {dates.map(({ date, rule }) => (
              <li key={`${rule.id}-${date}`} className="space-y-1.5 rounded-xl border border-border bg-card px-4 py-3 sm:px-5 sm:py-4">
                <p className="font-semibold wrap-anywhere">{rule.title}</p>
                <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
                  <time className="font-medium tabular-nums" dateTime={date}>{date}</time>
                  <span className="text-sm text-muted-foreground">{lunarLabel(date)}</span>
                </div>
                {rule.notes && <p className="text-sm wrap-anywhere">{rule.notes}</p>}
              </li>
            ))}
          </ul>
        )
      )}
      {(isAdmin || (rules.isSuccess && entries.length > 0)) && (
        <section aria-label="拜拜規則與照片" className="mt-8 space-y-4 border-t border-border pt-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h2 className="text-lg font-semibold">拜拜規則與照片</h2>
            {isAdmin && <Button type="button" className="min-h-11" onClick={() => setEditor('new')}>新增規則</Button>}
          </div>
          {isAdmin && editor && <WorshipEditor key={editor === 'new' ? 'new' : editor.id}
            initial={editor === 'new' ? emptyRule : editor} onCancel={() => setEditor(null)}
            onSave={async (input) => {
              await save.mutateAsync({
                input, id: editor === 'new' ? undefined : editor.id, version: editor === 'new' ? undefined : editor.version,
              })
              setEditor((current) => current === editor ? null : current)
            }} />}
          {remove.isError && <p role="alert">{isVersionConflict(remove.error) ? versionConflictMessage
            : isAxiosError(remove.error) && remove.error.response?.status === 409
            ? '請先移除這筆規則的照片，再刪除規則。' : '刪除未完成，請稍後再試。'}</p>}
          <ul className="space-y-2">
            {entries.map((rule) => <li key={rule.id} className="space-y-2 rounded-xl border border-border bg-card px-4 py-3 sm:px-5 sm:py-4">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="min-w-0 font-medium wrap-anywhere">{rule.title}</span>
                {isAdmin && <div className="flex flex-wrap items-center gap-2">
                  <Button type="button" variant="outline" className="min-h-11" aria-label={`編輯 ${rule.title}`} onClick={() => setEditor(rule)}>編輯</Button>
                  <Button type="button" variant="destructive" className="min-h-11" aria-label={`刪除 ${rule.title}`} disabled={remove.isPending}
                    onClick={() => { if (window.confirm(`確定刪除「${rule.title}」？`)) remove.mutate({ id: rule.id, version: rule.version }) }}>刪除</Button>
                </div>}
              </div>
              <PrivatePhotos kind="worship" id={rule.id} title={rule.title} />
            </li>)}
          </ul>
        </section>
      )}
    </section>
  )
}
