'use client'

import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { worshipInput, type WorshipInput } from '@/lib/hosted/worship-contract'

interface WorshipEditorProps {
  initial: WorshipInput
  onSave: (value: WorshipInput) => Promise<void>
  onCancel: () => void
}

const selectClass = 'min-h-11 w-full rounded-md border border-input bg-background px-2'

export default function WorshipEditor({ initial, onSave, onCancel }: WorshipEditorProps) {
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const month = form.get('month')
    const result = worshipInput.safeParse({
      title: form.get('title'), lunarMonth: month === 'monthly' ? null : Number(month),
      lunarDay: Number(form.get('day')), leapMonthPolicy: form.get('leap'), notes: form.get('notes'),
    })
    if (!result.success) {
      setError('請檢查規則名稱、農曆月日與備註。')
      return
    }
    setBusy(true)
    setError('')
    try {
      await onSave(result.data)
    } catch {
      setError('儲存未完成，請確認權限或稍後再試。')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4 rounded-lg border border-border p-4">
      <label className="grid gap-1 text-sm">規則名稱<Input name="title" defaultValue={initial.title} required maxLength={100} /></label>
      <div className="grid gap-3 sm:grid-cols-3">
        <label className="grid gap-1 text-sm">農曆月份
          <select name="month" className={selectClass} defaultValue={initial.lunarMonth ?? 'monthly'}>
            <option value="monthly">每個農曆月</option>
            {Array.from({ length: 12 }, (_, i) => <option value={i + 1} key={i + 1}>每年農曆 {i + 1} 月</option>)}
          </select>
        </label>
        <label className="grid gap-1 text-sm">農曆日<Input name="day" type="number" min={1} max={30} required defaultValue={initial.lunarDay} /></label>
        <label className="grid gap-1 text-sm">閏月處理
          <select name="leap" className={selectClass} defaultValue={initial.leapMonthPolicy}>
            <option value="RegularOnly">只用一般月份</option><option value="LeapOnly">只用閏月</option><option value="Both">一般月份與閏月</option>
          </select>
        </label>
      </div>
      <label className="grid gap-1 text-sm">備註<Input name="notes" defaultValue={initial.notes} maxLength={2000} /></label>
      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
      <div className="flex gap-2">
        <Button type="submit" disabled={busy}>{busy ? '儲存中…' : '儲存規則'}</Button>
        <Button type="button" variant="outline" onClick={onCancel} disabled={busy}>取消</Button>
      </div>
    </form>
  )
}
