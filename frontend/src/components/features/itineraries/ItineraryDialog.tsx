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
import { useCreateItinerary, useUpdateItinerary } from '@/hooks/useItineraries'
import type { Itinerary, UpsertItineraryRequest } from '@/types/event'

const itinerarySchema = z.object({
  title: z.string().min(1, '請輸入行程標題'),
  date: z.string().min(1, '請選擇出發日期'),
  location: z.string().optional(),
  description: z.string().optional(),
})

type ItineraryFormValues = z.infer<typeof itinerarySchema>

interface ItineraryDialogProps {
  itinerary?: Itinerary
  children: React.ReactElement
}

export function ItineraryDialog({ itinerary, children }: ItineraryDialogProps) {
  const [open, setOpen] = useState(false)
  const createMutation = useCreateItinerary()
  const updateMutation = useUpdateItinerary()

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<ItineraryFormValues>({
    resolver: zodResolver(itinerarySchema),
    defaultValues: {
      title: itinerary?.title || '',
      date: itinerary?.date ? itinerary.date.split('T')[0] : '',
      location: itinerary?.location || '',
      description: itinerary?.description || '',
    },
  })

  const onSubmit = async (data: ItineraryFormValues) => {
    const payload: UpsertItineraryRequest = {
      title: data.title,
      date: new Date(data.date).toISOString(),
      location: data.location,
      description: data.description,
      tags: itinerary?.tags || [],
      participants: itinerary?.participants || [],
      photos: itinerary?.photos || [],
    }

    if (itinerary) {
      await updateMutation.mutateAsync({ id: itinerary.id, data: payload })
    } else {
      await createMutation.mutateAsync(payload)
    }
    setOpen(false)
    if (!itinerary) reset()
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={children} />
      <DialogContent className="glass-panel-strong sm:max-w-[425px]">
        <DialogHeader>
          <DialogTitle>{itinerary ? '編輯歷年行程' : '新增歷年行程'}</DialogTitle>
        </DialogHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 pt-4">
          <div className="space-y-2">
            <Label>標題 <span className="text-destructive">*</span></Label>
            <Input className="glass-input" placeholder="例如: 2026 日本關西十日遊" {...register('title')} />
            {errors.title && <p className="text-xs text-destructive">{errors.title.message}</p>}
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label>日期 <span className="text-destructive">*</span></Label>
              <Input className="glass-input" type="date" {...register('date')} />
              {errors.date && <p className="text-xs text-destructive">{errors.date.message}</p>}
            </div>
            <div className="space-y-2">
              <Label>地點</Label>
              <Input className="glass-input" placeholder="例如: 日本京都" {...register('location')} />
            </div>
          </div>

          <div className="space-y-2">
            <Label>行程描述 / 筆記</Label>
            <Textarea className="glass-input resize-none" rows={4} placeholder="分享一下這趟旅程的心得..." {...register('description')} />
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
