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
import { useCreateItinerary, useUpdateItinerary } from '@/hooks/useItineraries'
import type { Itinerary, UpsertItineraryRequest } from '@/types/event'

export const itinerarySchema = z.object({
  title: z.string().trim().min(1, '請輸入行程標題').max(200, '最多 200 個字元'),
  date: z.string().min(1, '請選擇出發日期'),
  location: z.string().max(200, '最多 200 個字元').optional(),
  description: z.string().max(4000, '最多 4000 個字元').optional(),
})

type ItineraryFormValues = z.infer<typeof itinerarySchema>

interface ItineraryDialogProps {
  itinerary?: Itinerary
  children: React.ReactElement
}

export function ItineraryDialog({ itinerary, children }: ItineraryDialogProps) {
  const [open, setOpen] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const createMutation = useCreateItinerary()
  const updateMutation = useUpdateItinerary()
  const isPending = createMutation.isPending || updateMutation.isPending
  const { register, handleSubmit, reset, formState: { errors } } = useForm<ItineraryFormValues>({
    resolver: zodResolver(itinerarySchema),
    defaultValues: {
      title: itinerary?.title || '', date: itinerary?.date ? itinerary.date.split('T')[0] : '',
      location: itinerary?.location || '', description: itinerary?.description || '',
    },
  })

  const onSubmit = async (data: ItineraryFormValues) => {
    setFormError(null)
    const payload: UpsertItineraryRequest = {
      title: data.title,
      date: new Date(`${data.date}T00:00:00.000Z`).toISOString(),
      location: data.location,
      description: data.description,
      tags: itinerary?.tags || [],
      participants: itinerary?.participants || [],
      photos: itinerary?.photos || [],
    }
    try {
      if (itinerary) await updateMutation.mutateAsync({ id: itinerary.id, data: payload })
      else await createMutation.mutateAsync(payload)
      setOpen(false)
      if (!itinerary) reset()
    } catch (error: unknown) {
      setFormError(error instanceof Error && error.message ? error.message : '無法儲存行程，請稍後再試。')
    }
  }

  return (
    <Dialog open={open} onOpenChange={(nextOpen) => { if (!isPending) { setOpen(nextOpen); if (!nextOpen) setFormError(null) } }}>
      <DialogTrigger render={children} />
      <DialogContent className="glass-panel-strong sm:max-w-[425px]">
        <DialogHeader>
          <DialogTitle>{itinerary ? '編輯歷年行程' : '新增歷年行程'}</DialogTitle>
          <DialogDescription>記錄行程日期、地點與心得。</DialogDescription>
        </DialogHeader>
        <form noValidate onSubmit={handleSubmit(onSubmit)} className="space-y-4 pt-2">
          {formError && <p id="itinerary-form-error" role="alert" className="text-sm text-destructive">{formError}</p>}
          <div className="space-y-2">
            <div className="flex items-center justify-between gap-2"><Label htmlFor="itinerary-title">標題</Label><span className="text-xs text-destructive">必填</span></div>
            <Input id="itinerary-title" className="h-11 glass-input" autoComplete="off" required maxLength={200} placeholder="例如：2026 日本關西十日遊" aria-invalid={Boolean(errors.title)} aria-describedby={errors.title ? 'itinerary-title-error' : undefined} {...register('title')} />
            {errors.title && <p id="itinerary-title-error" role="alert" className="text-xs text-destructive">{errors.title.message}</p>}
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <div className="flex items-center justify-between gap-2"><Label htmlFor="itinerary-date">日期</Label><span className="text-xs text-destructive">必填</span></div>
              <Input id="itinerary-date" className="h-11 glass-input" type="date" autoComplete="off" required aria-invalid={Boolean(errors.date)} aria-describedby={errors.date ? 'itinerary-date-error' : undefined} {...register('date')} />
              {errors.date && <p id="itinerary-date-error" role="alert" className="text-xs text-destructive">{errors.date.message}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="itinerary-location">地點</Label>
              <Input id="itinerary-location" className="h-11 glass-input" autoComplete="off" maxLength={200} placeholder="例如：日本京都" aria-invalid={Boolean(errors.location)} aria-describedby={errors.location ? 'itinerary-location-error' : undefined} {...register('location')} />
              {errors.location && <p id="itinerary-location-error" role="alert" className="text-xs text-destructive">{errors.location.message}</p>}
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="itinerary-description">行程描述／筆記</Label>
            <Textarea id="itinerary-description" className="glass-input resize-none" autoComplete="off" rows={4} maxLength={4000} placeholder="分享這趟旅程的心得…" aria-invalid={Boolean(errors.description)} aria-describedby={errors.description ? 'itinerary-description-error' : undefined} {...register('description')} />
            {errors.description && <p id="itinerary-description-error" role="alert" className="text-xs text-destructive">{errors.description.message}</p>}
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
