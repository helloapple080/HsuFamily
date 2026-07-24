'use client'

import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { useCreateEvent, useUpdateEvent } from '@/hooks/useCalendarEvents'
import { solarToLunar } from '@/lib/lunar'
import type { CalendarEvent, UpsertEventRequest } from '@/types/event'

export const eventSchema = z.object({
  title: z.string().trim().min(1, '請輸入事件標題').max(200, '最多 200 個字元'),
  eventType: z.enum(['General', 'Birthday', 'Festival', 'Worship', 'Anniversary', 'Meeting', 'Other']),
  startDate: z.string().min(1, '請選擇開始日期'),
  isLunarDate: z.boolean().optional(),
  description: z.string().max(4000, '最多 4000 個字元').optional(),
})

type EventFormValues = z.infer<typeof eventSchema>

interface EventDialogProps {
  event?: CalendarEvent
  children: React.ReactElement
  defaultDate?: Date
}

function formatLocalDateInput(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

export function EventDialog({ event, children, defaultDate }: EventDialogProps) {
  const [open, setOpen] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const createMutation = useCreateEvent()
  const updateMutation = useUpdateEvent()
  const isPending = createMutation.isPending || updateMutation.isPending
  const { register, handleSubmit, reset, formState: { errors } } = useForm<EventFormValues>({
    resolver: zodResolver(eventSchema),
    defaultValues: {
      title: event?.title || '',
      eventType: event?.eventType || 'General',
      startDate: event?.startDate ? event.startDate.split('T')[0] : (defaultDate ? formatLocalDateInput(defaultDate) : ''),
      isLunarDate: event?.isLunarDate || false,
      description: event?.description || '',
    },
  })

  const onSubmit = async (data: EventFormValues) => {
    setFormError(null)
    const startDate = new Date(data.startDate)
    const lunarDate = data.isLunarDate
      ? solarToLunar(startDate.getFullYear(), startDate.getMonth() + 1, startDate.getDate())
      : null
    const payload: UpsertEventRequest = {
      title: data.title,
      eventType: data.eventType,
      startDate: startDate.toISOString(),
      isAllDay: true,
      isLunarDate: data.isLunarDate ?? false,
      lunarDate,
      description: data.description,
      relatedMemberIds: event?.relatedMemberIds || [],
      tags: event?.tags || [],
    }

    try {
      if (event) await updateMutation.mutateAsync({ id: event.id, data: payload })
      else await createMutation.mutateAsync(payload)
      setOpen(false)
      if (!event) reset()
    } catch (error: unknown) {
      setFormError(error instanceof Error && error.message ? error.message : '無法儲存事件，請稍後再試。')
    }
  }

  return (
    <Dialog open={open} onOpenChange={(nextOpen) => { if (!isPending) { setOpen(nextOpen); if (!nextOpen) setFormError(null) } }}>
      <DialogTrigger render={children} />
      <DialogContent className="glass-panel-strong sm:max-w-[425px]">
        <DialogHeader>
          <DialogTitle>{event ? '編輯行事曆事件' : '新增行事曆事件'}</DialogTitle>
          <DialogDescription>填寫事件的日期、類型與備註。</DialogDescription>
        </DialogHeader>
        <form noValidate onSubmit={handleSubmit(onSubmit)} className="space-y-4 pt-2">
          {formError && <p id="event-form-error" role="alert" className="text-sm text-destructive">{formError}</p>}
          <div className="space-y-2">
            <div className="flex items-center justify-between gap-2"><Label htmlFor="event-title">標題</Label><span className="text-xs text-destructive">必填</span></div>
            <Input id="event-title" className="glass-input" autoComplete="off" required placeholder="例如：媽媽生日" maxLength={200} aria-invalid={Boolean(errors.title)} aria-describedby={errors.title ? 'event-title-error' : undefined} {...register('title')} />
            {errors.title && <p id="event-title-error" role="alert" className="text-xs text-destructive">{errors.title.message}</p>}
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <div className="flex items-center justify-between gap-2"><Label htmlFor="event-type">類型</Label><span className="text-xs text-destructive">必填</span></div>
              <select id="event-type" required className="flex h-11 w-full items-center rounded-md border border-input bg-transparent px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring glass-input" {...register('eventType')}>
                <option value="General">一般事件</option><option value="Birthday">生日</option><option value="Festival">節日</option><option value="Worship">拜拜</option><option value="Anniversary">紀念日</option><option value="Meeting">會議</option><option value="Other">其他</option>
              </select>
            </div>
            <div className="space-y-2">
              <div className="flex items-center justify-between gap-2"><Label htmlFor="event-date">日期</Label><span className="text-xs text-destructive">必填</span></div>
              <Input id="event-date" className="h-11 glass-input" type="date" autoComplete="off" required aria-invalid={Boolean(errors.startDate)} aria-describedby={errors.startDate ? 'event-date-error' : undefined} {...register('startDate')} />
              {errors.startDate && <p id="event-date-error" role="alert" className="text-xs text-destructive">{errors.startDate.message}</p>}
            </div>
          </div>
          <div className="flex min-h-11 items-center gap-2">
            <input type="checkbox" id="event-is-lunar" {...register('isLunarDate')} className="size-5 rounded border-[var(--border)]" />
            <Label htmlFor="event-is-lunar" className="cursor-pointer">農曆週期（依農曆推算）</Label>
          </div>
          <div className="space-y-2">
            <Label htmlFor="event-description">備註</Label>
            <Textarea id="event-description" className="glass-input resize-none" autoComplete="off" rows={3} maxLength={4000} aria-invalid={Boolean(errors.description)} aria-describedby={errors.description ? 'event-description-error' : undefined} placeholder="選填補充…" {...register('description')} />
            {errors.description && <p id="event-description-error" role="alert" className="text-xs text-destructive">{errors.description.message}</p>}
          </div>
          <div className="flex justify-end gap-2 pt-4">
            <Button type="button" variant="ghost" className="h-11" disabled={isPending} onClick={() => setOpen(false)}>取消</Button>
            <Button type="submit" className="h-11 glass-button" disabled={isPending}>{isPending ? '儲存中…' : '儲存'}</Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  )
}
