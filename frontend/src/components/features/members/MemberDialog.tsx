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
import { useCreateMember, useUpdateMember } from '@/hooks/useMembers'
import { formatLunarDate } from '@/lib/lunar'
import type { Member, UpsertMemberRequest } from '@/types/member'

export const memberSchema = z.object({
  name: z.string().trim().min(1, '請輸入姓名').max(200, '最多 200 個字元'),
  title: z.string().trim().min(1, '請輸入稱謂（如：爸爸）').max(200, '最多 200 個字元'),
  bloodType: z.string().max(200, '最多 200 個字元').optional(),
  bio: z.string().max(4000, '最多 4000 個字元').optional(),
  birthday: z.string().min(1, '請選擇國曆生日'),
  lunarBirthdayMonth: z.number().int().min(1, '月份必須介於 1 到 12').max(12, '月份必須介於 1 到 12').nullable().optional(),
  lunarBirthdayDay: z.number().int().min(1, '日期必須介於 1 到 30').max(30, '日期必須介於 1 到 30').nullable().optional(),
  isLunarLeapMonth: z.boolean().optional(),
}).superRefine((data, context) => {
  const hasMonth = data.lunarBirthdayMonth != null
  const hasDay = data.lunarBirthdayDay != null
  if (hasMonth === hasDay) return
  context.addIssue({ code: 'custom', message: '請同時填寫農曆月份與日期', path: [hasMonth ? 'lunarBirthdayDay' : 'lunarBirthdayMonth'] })
})

type MemberFormValues = z.infer<typeof memberSchema>

interface MemberDialogProps {
  member?: Member
  children: React.ReactElement
}

/// 將表單資料轉為完整且有效的成員 API payload。
export function buildMemberPayload(data: MemberFormValues, member?: Member): UpsertMemberRequest {
  const birthday = new Date(`${data.birthday}T00:00:00.000Z`).toISOString()
  const hasLunarBirthday = data.lunarBirthdayMonth != null && data.lunarBirthdayDay != null
  const lunarYear = Number.parseInt(data.birthday.slice(0, 4), 10)
  return {
    name: data.name,
    title: data.title,
    bloodType: data.bloodType,
    bio: data.bio,
    sortOrder: member?.sortOrder ?? 0,
    isActive: member?.isActive ?? true,
    birthday,
    lunarBirthday: hasLunarBirthday ? {
      year: lunarYear,
      month: data.lunarBirthdayMonth!,
      day: data.lunarBirthdayDay!,
      isLeapMonth: data.isLunarLeapMonth ?? false,
      displayText: formatLunarDate(data.lunarBirthdayMonth!, data.lunarBirthdayDay!, data.isLunarLeapMonth ?? false),
    } : undefined,
  }
}

export function MemberDialog({ member, children }: MemberDialogProps) {
  const [open, setOpen] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const createMutation = useCreateMember()
  const updateMutation = useUpdateMember()
  const isPending = createMutation.isPending || updateMutation.isPending
  const { register, handleSubmit, reset, formState: { errors } } = useForm<MemberFormValues>({
    resolver: zodResolver(memberSchema),
    defaultValues: {
      name: member?.name || '', title: member?.title || '', bloodType: member?.bloodType || '', bio: member?.bio || '',
      birthday: member?.birthday ? member.birthday.split('T')[0] : '', lunarBirthdayMonth: member?.lunarBirthday?.month || null,
      lunarBirthdayDay: member?.lunarBirthday?.day || null, isLunarLeapMonth: member?.lunarBirthday?.isLeapMonth || false,
    },
  })

  const onSubmit = async (data: MemberFormValues) => {
    setFormError(null)
    const payload = buildMemberPayload(data, member)
    try {
      if (member) await updateMutation.mutateAsync({ id: member.id, data: payload })
      else await createMutation.mutateAsync(payload)
      setOpen(false)
      if (!member) reset()
    } catch (error: unknown) {
      setFormError(error instanceof Error && error.message ? error.message : '無法儲存成員資料，請稍後再試。')
    }
  }

  const nullableNumber = { setValueAs: (value: string | null | undefined) => value == null || value === '' ? null : Number(value) }

  return (
    <Dialog open={open} onOpenChange={(nextOpen) => { if (!isPending) { setOpen(nextOpen); if (!nextOpen) setFormError(null) } }}>
      <DialogTrigger render={children} />
      <DialogContent className="max-h-[calc(100dvh-2rem)] overflow-y-auto glass-panel-strong sm:max-w-[475px]">
        <DialogHeader>
          <DialogTitle>{member ? '編輯家庭成員' : '新增家庭成員'}</DialogTitle>
          <DialogDescription>記錄成員的基本資料與國農曆生日。</DialogDescription>
        </DialogHeader>
        <form noValidate onSubmit={handleSubmit(onSubmit)} className="space-y-4 pt-2">
          {formError && <p id="member-form-error" role="alert" className="text-sm text-destructive">{formError}</p>}
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <div className="flex items-center justify-between gap-2"><Label htmlFor="member-name">姓名</Label><span className="text-xs text-destructive">必填</span></div>
              <Input id="member-name" className="h-11 glass-input" autoComplete="off" required maxLength={200} aria-invalid={Boolean(errors.name)} aria-describedby={errors.name ? 'member-name-error' : undefined} {...register('name')} />
              {errors.name && <p id="member-name-error" role="alert" className="text-xs text-destructive">{errors.name.message}</p>}
            </div>
            <div className="space-y-2">
              <div className="flex items-center justify-between gap-2"><Label htmlFor="member-title">稱謂</Label><span className="text-xs text-destructive">必填</span></div>
              <Input id="member-title" className="h-11 glass-input" autoComplete="off" required maxLength={200} placeholder="例如：媽媽" aria-invalid={Boolean(errors.title)} aria-describedby={errors.title ? 'member-title-error' : undefined} {...register('title')} />
              {errors.title && <p id="member-title-error" role="alert" className="text-xs text-destructive">{errors.title.message}</p>}
            </div>
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <div className="flex items-center justify-between gap-2"><Label htmlFor="member-birthday">國曆生日</Label><span className="text-xs text-destructive">必填</span></div>
              <Input id="member-birthday" className="h-11 glass-input" type="date" autoComplete="off" required aria-invalid={Boolean(errors.birthday)} aria-describedby={errors.birthday ? 'member-birthday-error' : undefined} {...register('birthday')} />
              {errors.birthday && <p id="member-birthday-error" role="alert" className="text-xs text-destructive">{errors.birthday.message}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="member-blood-type">血型</Label>
              <Input id="member-blood-type" className="h-11 glass-input" autoComplete="off" maxLength={200} placeholder="例如：O" aria-invalid={Boolean(errors.bloodType)} aria-describedby={errors.bloodType ? 'member-blood-type-error' : undefined} {...register('bloodType')} />
              {errors.bloodType && <p id="member-blood-type-error" role="alert" className="text-xs text-destructive">{errors.bloodType.message}</p>}
            </div>
          </div>
          <fieldset className="rounded-md border border-[var(--border)] bg-[var(--secondary)]/50 p-3">
            <legend className="px-1 text-sm font-medium text-[var(--lunar)]">農曆生日（選填）</legend>
            <div className="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-3">
              <div className="space-y-2">
                <Label htmlFor="member-lunar-month">月份</Label>
                <Input id="member-lunar-month" className="h-11 glass-input" type="number" min={1} max={12} inputMode="numeric" aria-invalid={Boolean(errors.lunarBirthdayMonth)} aria-describedby={errors.lunarBirthdayMonth ? 'member-lunar-month-error' : undefined} {...register('lunarBirthdayMonth', nullableNumber)} />
                {errors.lunarBirthdayMonth && <p id="member-lunar-month-error" role="alert" className="text-xs text-destructive">{errors.lunarBirthdayMonth.message}</p>}
              </div>
              <div className="space-y-2">
                <Label htmlFor="member-lunar-day">日期</Label>
                <Input id="member-lunar-day" className="h-11 glass-input" type="number" min={1} max={30} inputMode="numeric" aria-invalid={Boolean(errors.lunarBirthdayDay)} aria-describedby={errors.lunarBirthdayDay ? 'member-lunar-day-error' : undefined} {...register('lunarBirthdayDay', nullableNumber)} />
                {errors.lunarBirthdayDay && <p id="member-lunar-day-error" role="alert" className="text-xs text-destructive">{errors.lunarBirthdayDay.message}</p>}
              </div>
              <div className="flex min-h-11 items-center gap-2 self-end">
                <input id="member-lunar-leap" type="checkbox" {...register('isLunarLeapMonth')} className="size-5 rounded border-[var(--border)]" />
                <Label htmlFor="member-lunar-leap" className="cursor-pointer">閏月</Label>
              </div>
            </div>
          </fieldset>
          <div className="space-y-2">
            <Label htmlFor="member-bio">個人介紹</Label>
            <Textarea id="member-bio" className="glass-input resize-none" autoComplete="off" rows={3} maxLength={4000} aria-invalid={Boolean(errors.bio)} aria-describedby={errors.bio ? 'member-bio-error' : undefined} {...register('bio')} />
            {errors.bio && <p id="member-bio-error" role="alert" className="text-xs text-destructive">{errors.bio.message}</p>}
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
