'use client'

import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import {
  Dialog,
  DialogContent,
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

const eventSchema = z.object({
  title: z.string().min(1, '請輸入事件標題'),
  eventType: z.enum(['General', 'Birthday', 'Festival', 'Worship', 'Anniversary', 'Meeting', 'Other']),
  startDate: z.string().min(1, '請選擇開始日期'),
  isLunarDate: z.boolean().optional(),
  description: z.string().optional(),
})

type EventFormValues = z.infer<typeof eventSchema>

interface EventDialogProps {
  event?: CalendarEvent
  children: React.ReactElement
  defaultDate?: Date
}

export function EventDialog({ event, children, defaultDate }: EventDialogProps) {
  const [open, setOpen] = useState(false)
  const createMutation = useCreateEvent()
  const updateMutation = useUpdateEvent()

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<EventFormValues>({
    resolver: zodResolver(eventSchema),
    defaultValues: {
      title: event?.title || '',
      eventType: event?.eventType || 'General',
      startDate: event?.startDate ? event.startDate.split('T')[0] : (defaultDate ? defaultDate.toISOString().split('T')[0] : ''),
      isLunarDate: event?.isLunarDate || false,
      description: event?.description || '',
    },
  })

  const onSubmit = async (data: EventFormValues) => {
    const startDate = new Date(data.startDate)
    const lunarDate = data.isLunarDate
      ? solarToLunar(
          startDate.getFullYear(),
          startDate.getMonth() + 1,
          startDate.getDate()
        )
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

    if (event) {
      await updateMutation.mutateAsync({ id: event.id, data: payload })
    } else {
      await createMutation.mutateAsync(payload)
    }
    setOpen(false)
    if (!event) reset()
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={children} />
      <DialogContent className="glass-panel-strong sm:max-w-[425px]">
        <DialogHeader>
          <DialogTitle>{event ? '編輯行事曆事件' : '新增行事曆事件'}</DialogTitle>
        </DialogHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 pt-4">
          <div className="space-y-2">
            <Label>標題 <span className="text-destructive">*</span></Label>
            <Input className="glass-input" placeholder="例如: 媽媽生日" {...register('title')} />
            {errors.title && <p className="text-xs text-destructive">{errors.title.message}</p>}
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label>類型 <span className="text-destructive">*</span></Label>
              <select 
                className="flex h-10 w-full items-center justify-between rounded-md border border-input bg-transparent px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 glass-input"
                {...register('eventType')}
              >
                <option value="General">一般事件</option>
                <option value="Birthday">生日</option>
                <option value="Festival">節日</option>
                <option value="Worship">拜拜</option>
                <option value="Anniversary">紀念日</option>
                <option value="Meeting">會議</option>
                <option value="Other">其他</option>
              </select>
            </div>
            <div className="space-y-2">
              <Label>日期 <span className="text-destructive">*</span></Label>
              <Input className="glass-input" type="date" {...register('startDate')} />
              {errors.startDate && <p className="text-xs text-destructive">{errors.startDate.message}</p>}
            </div>
          </div>

          <div className="flex items-center space-x-2 py-2">
            <input type="checkbox" id="isLunarDate" {...register('isLunarDate')} className="rounded border-[var(--border)] w-4 h-4" />
            <Label htmlFor="isLunarDate" className="cursor-pointer text-sm">農曆週期 (依農曆推算)</Label>
          </div>

          <div className="space-y-2">
            <Label>備註</Label>
            <Textarea className="glass-input resize-none" rows={3} placeholder="選填補充..." {...register('description')} />
          </div>

          <div className="flex justify-end gap-2 pt-4">
            <Button type="button" variant="ghost" onClick={() => setOpen(false)}>
              取消
            </Button>
            <Button type="submit" className="glass-button" disabled={createMutation.isPending || updateMutation.isPending}>
              儲存
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  )
}
