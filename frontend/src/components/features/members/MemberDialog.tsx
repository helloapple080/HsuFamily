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
import { useCreateMember, useUpdateMember } from '@/hooks/useMembers'
import type { Member, UpsertMemberRequest } from '@/types/member'

const memberSchema = z.object({
  name: z.string().min(1, '請輸入姓名'),
  title: z.string().min(1, '請輸入稱謂 (如: 爸爸)'),
  bloodType: z.string().optional(),
  bio: z.string().optional(),
  birthday: z.string().optional(),
  lunarBirthdayMonth: z.number().nullable().optional(),
  lunarBirthdayDay: z.number().nullable().optional(),
  isLunarLeapMonth: z.boolean().optional(),
})

type MemberFormValues = z.infer<typeof memberSchema>

interface MemberDialogProps {
  member?: Member
  children: React.ReactElement
}

export function MemberDialog({ member, children }: MemberDialogProps) {
  const [open, setOpen] = useState(false)
  const createMutation = useCreateMember()
  const updateMutation = useUpdateMember()

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<MemberFormValues>({
    resolver: zodResolver(memberSchema),
    defaultValues: {
      name: member?.name || '',
      title: member?.title || '',
      bloodType: member?.bloodType || '',
      bio: member?.bio || '',
      birthday: member?.birthday ? member.birthday.split('T')[0] : '',
      lunarBirthdayMonth: member?.lunarBirthday?.month || null,
      lunarBirthdayDay: member?.lunarBirthday?.day || null,
      isLunarLeapMonth: member?.lunarBirthday?.isLeapMonth || false,
    },
  })

  const onSubmit = async (data: MemberFormValues) => {
    // API expects birthday as string or null.
    // Ensure properly formatted ISO string or empty.
    let isoDate = ''
    if (data.birthday) {
      // Create a valid ISO string from the input YYYY-MM-DD
      isoDate = new Date(data.birthday).toISOString()
    }

    const payload: UpsertMemberRequest = {
      name: data.name,
      title: data.title,
      bloodType: data.bloodType,
      bio: data.bio,
      sortOrder: member?.sortOrder ?? 0,
      isActive: member?.isActive ?? true,
      birthday: isoDate,
      lunarBirthday: (data.lunarBirthdayMonth && data.lunarBirthdayDay) ? {
        year: member?.lunarBirthday?.year ?? 0,
        month: Number(data.lunarBirthdayMonth),
        day: Number(data.lunarBirthdayDay),
        isLeapMonth: data.isLunarLeapMonth ?? false,
        displayText: '' // 由後續農曆顯示邏輯補齊
      } : undefined,
    }

    if (member) {
      await updateMutation.mutateAsync({ id: member.id, data: payload })
    } else {
      await createMutation.mutateAsync(payload)
    }
    setOpen(false)
    if (!member) reset()
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={children} />
      <DialogContent className="glass-panel-strong sm:max-w-[425px]">
        <DialogHeader>
          <DialogTitle>{member ? '編輯家庭成員' : '新增家庭成員'}</DialogTitle>
        </DialogHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 pt-4">
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label>姓名 <span className="text-destructive">*</span></Label>
              <Input className="glass-input" {...register('name')} />
              {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
            </div>
            <div className="space-y-2">
              <Label>稱謂 <span className="text-destructive">*</span></Label>
              <Input className="glass-input" placeholder="例如: 媽媽" {...register('title')} />
              {errors.title && <p className="text-xs text-destructive">{errors.title.message}</p>}
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label>國曆生日</Label>
              <Input className="glass-input" type="date" {...register('birthday')} />
            </div>
            <div className="space-y-2">
              <Label>血型</Label>
              <Input className="glass-input" placeholder="例如: O" {...register('bloodType')} />
            </div>
          </div>

          <div className="space-y-2 border border-[var(--border)] rounded-md p-3 bg-[var(--secondary)]/50">
            <Label className="text-[var(--lunar)]">農曆生日 (選填)</Label>
            <div className="grid grid-cols-3 gap-2 mt-2">
              <Input className="glass-input" type="number" placeholder="月份" {...register('lunarBirthdayMonth', { valueAsNumber: true })} />
              <Input className="glass-input" type="number" placeholder="日期" {...register('lunarBirthdayDay', { valueAsNumber: true })} />
              <label className="flex items-center gap-2 text-sm justify-center cursor-pointer">
                <input type="checkbox" {...register('isLunarLeapMonth')} className="rounded border-[var(--border)]" />
                閏月
              </label>
            </div>
          </div>

          <div className="space-y-2">
            <Label>個人介紹</Label>
            <Textarea className="glass-input resize-none" rows={3} {...register('bio')} />
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
