'use client'

import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
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
  recurrenceType: z.enum(['None', 'LunarYearly', 'LunarMonthly']),
  recurrenceInterval: z.number().int().min(1, '重複間隔至少為 1').max(120, '重複間隔最多為 120'),
  lunarDays: z.string().max(100, '農曆日期設定過長').optional(),
  leapMonthPolicy: z.enum(['RegularOnly', 'LeapOnly', 'Both']),
  effectiveFrom: z.string().optional(),
  effectiveUntil: z.string().optional(),
}).superRefine((data, context) => {
  if (!data.isLunarDate || data.recurrenceType === 'None') return

  if (data.recurrenceType === 'LunarMonthly' && parseLunarDays(data.lunarDays).length === 0) {
    context.addIssue({
      code: 'custom',
      path: ['lunarDays'],
      message: '請輸入 1 至 30 的農曆日期，可用逗號分隔',
    })
  }

  if (data.effectiveFrom && data.effectiveUntil && data.effectiveUntil < data.effectiveFrom) {
    context.addIssue({
      code: 'custom',
      path: ['effectiveUntil'],
      message: '結束日期不可早於生效日期',
    })
  }
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

function formatOptionalDateInput(value?: string | null): string {
  return value?.split('T')[0] ?? ''
}

function parseLunarDays(value?: string): number[] {
  if (!value?.trim()) return []

  const values = value
    .split(/[,，]/)
    .map((part) => Number(part.trim()))
  if (values.some((day) => !Number.isInteger(day) || day < 1 || day > 30)) return []
  return [...new Set(values)]
}

function toUtcDate(value?: string): string | null {
  return value ? new Date(`${value}T00:00:00.000Z`).toISOString() : null
}

export function EventDialog({ event, children, defaultDate }: EventDialogProps) {
  const [open, setOpen] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const createMutation = useCreateEvent()
  const updateMutation = useUpdateEvent()
  const isPending = createMutation.isPending || updateMutation.isPending
  const { control, register, handleSubmit, reset, formState: { errors } } = useForm<EventFormValues>({
    resolver: zodResolver(eventSchema),
    defaultValues: {
      title: event?.title || '',
      eventType: event?.eventType || 'General',
      startDate: event?.startDate ? event.startDate.split('T')[0] : (defaultDate ? formatLocalDateInput(defaultDate) : ''),
      isLunarDate: event?.isLunarDate || false,
      description: event?.description || '',
      recurrenceType: event?.recurrenceRule?.type === 'LunarYearly' || event?.recurrenceRule?.type === 'LunarMonthly'
        ? event.recurrenceRule.type
        : 'None',
      recurrenceInterval: event?.recurrenceRule?.interval ?? 1,
      lunarDays: event?.recurrenceRule?.lunarDays?.join(', ') ?? '',
      leapMonthPolicy: event?.recurrenceRule?.leapMonthPolicy ?? 'RegularOnly',
      effectiveFrom: formatOptionalDateInput(event?.recurrenceRule?.effectiveFrom),
      effectiveUntil: formatOptionalDateInput(event?.recurrenceRule?.effectiveUntil),
    },
  })
  const isLunarDate = useWatch({ control, name: 'isLunarDate' })
  const recurrenceType = useWatch({ control, name: 'recurrenceType' })

  const onSubmit = async (data: EventFormValues) => {
    setFormError(null)
    const startDate = new Date(data.startDate)
    const lunarDate = data.isLunarDate
      ? solarToLunar(startDate.getFullYear(), startDate.getMonth() + 1, startDate.getDate())
      : null
    const recurrenceRule = data.isLunarDate && data.recurrenceType !== 'None'
      ? {
          type: data.recurrenceType,
          interval: data.recurrenceInterval,
          lunarDays: data.recurrenceType === 'LunarMonthly' ? parseLunarDays(data.lunarDays) : null,
          leapMonthPolicy: data.leapMonthPolicy,
          effectiveFrom: toUtcDate(data.effectiveFrom),
          effectiveUntil: toUtcDate(data.effectiveUntil),
        }
      : null
    const payload: UpsertEventRequest = {
      title: data.title,
      eventType: data.eventType,
      startDate: startDate.toISOString(),
      endDate: event?.endDate ?? null,
      isAllDay: event?.isAllDay ?? true,
      isLunarDate: data.isLunarDate ?? false,
      lunarDate,
      recurrenceRule,
      description: data.description,
      worshipDetails: data.eventType === 'Worship' ? event?.worshipDetails ?? null : null,
      relatedMemberIds: event?.relatedMemberIds || [],
      tags: event?.tags || [],
      color: event?.color ?? null,
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
      <DialogContent className="max-h-[90vh] overflow-y-auto glass-panel-strong sm:max-w-[560px]">
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
          <div className="flex min-h-11 items-start gap-2">
            <input type="checkbox" id="event-is-lunar" {...register('isLunarDate')} className="size-5 rounded border-[var(--border)]" />
            <div className="space-y-1">
              <Label htmlFor="event-is-lunar" className="cursor-pointer">使用農曆日期</Label>
              <p className="text-xs text-muted-foreground">依上方國曆日期換算農曆月日；勾選後可設定是否重複。</p>
            </div>
          </div>
          {isLunarDate && (
            <fieldset className="space-y-4 rounded-lg border border-border/70 p-4">
              <legend className="px-1 text-sm font-medium">農曆重複設定</legend>
              <div className="space-y-2">
                <Label htmlFor="event-recurrence-type">農曆重複方式</Label>
                <select id="event-recurrence-type" className="flex h-11 w-full items-center rounded-md border border-input bg-transparent px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring glass-input" {...register('recurrenceType')}>
                  <option value="None">不重複，只建立這一天</option>
                  <option value="LunarYearly">每年在相同農曆月日</option>
                  <option value="LunarMonthly">每個農曆月在指定日期</option>
                </select>
              </div>
              {recurrenceType !== 'None' && (
                <>
                  <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                    <div className="space-y-2">
                      <Label htmlFor="event-recurrence-interval">重複間隔</Label>
                      <Input id="event-recurrence-interval" className="h-11 glass-input" type="number" min={1} max={120} aria-invalid={Boolean(errors.recurrenceInterval)} aria-describedby={errors.recurrenceInterval ? 'event-recurrence-interval-error' : undefined} {...register('recurrenceInterval', { valueAsNumber: true })} />
                      <p className="text-xs text-muted-foreground">1 代表每年或每月，2 代表每兩年或每兩月。</p>
                      {errors.recurrenceInterval && <p id="event-recurrence-interval-error" role="alert" className="text-xs text-destructive">{errors.recurrenceInterval.message}</p>}
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="event-leap-policy">閏月處理</Label>
                      <select id="event-leap-policy" className="flex h-11 w-full items-center rounded-md border border-input bg-transparent px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring glass-input" {...register('leapMonthPolicy')}>
                        <option value="RegularOnly">只在一般月份</option>
                        <option value="LeapOnly">只在閏月</option>
                        <option value="Both">一般月份與閏月都重複</option>
                      </select>
                    </div>
                  </div>
                  {recurrenceType === 'LunarMonthly' && (
                    <div className="space-y-2">
                      <Label htmlFor="event-lunar-days">每月農曆日</Label>
                      <Input id="event-lunar-days" className="h-11 glass-input" inputMode="numeric" placeholder="例如：1, 15" aria-invalid={Boolean(errors.lunarDays)} aria-describedby={errors.lunarDays ? 'event-lunar-days-error' : 'event-lunar-days-help'} {...register('lunarDays')} />
                      <p id="event-lunar-days-help" className="text-xs text-muted-foreground">輸入 1 至 30，可用逗號分隔多個日期，例如初一、十五請填 1, 15。</p>
                      {errors.lunarDays && <p id="event-lunar-days-error" role="alert" className="text-xs text-destructive">{errors.lunarDays.message}</p>}
                    </div>
                  )}
                  <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                    <div className="space-y-2">
                      <Label htmlFor="event-effective-from">生效日期</Label>
                      <Input id="event-effective-from" className="h-11 glass-input" type="date" {...register('effectiveFrom')} />
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="event-effective-until">結束日期</Label>
                      <Input id="event-effective-until" className="h-11 glass-input" type="date" aria-invalid={Boolean(errors.effectiveUntil)} aria-describedby={errors.effectiveUntil ? 'event-effective-until-error' : undefined} {...register('effectiveUntil')} />
                      {errors.effectiveUntil && <p id="event-effective-until-error" role="alert" className="text-xs text-destructive">{errors.effectiveUntil.message}</p>}
                    </div>
                  </div>
                </>
              )}
            </fieldset>
          )}
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
