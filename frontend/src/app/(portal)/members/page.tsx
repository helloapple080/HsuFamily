'use client'

import { Pencil, Plus, Trash2, Users } from 'lucide-react'
import AdminOnly from '@/components/common/AdminOnly'
import ConfirmDialog from '@/components/common/ConfirmDialog'
import { MemberDialog } from '@/components/features/members/MemberDialog'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useDeleteMember, useMembers } from '@/hooks/useMembers'

export default function MembersPage() {
  const { data: members = [], isLoading } = useMembers()
  const deleteMutation = useDeleteMember()

  return (
    <div className="space-y-6">
      <header className="flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold tracking-tight">
            <Users aria-hidden="true" className="size-6" />
            家庭成員
          </h1>
          <p className="mt-1 text-sm text-[var(--muted-foreground)]">家庭成員資料管理 — 包含農曆生日</p>
        </div>
        <AdminOnly>
          <MemberDialog>
            <Button className="h-11 gap-2 glass-button">
              <Plus aria-hidden="true" />
              新增成員
            </Button>
          </MemberDialog>
        </AdminOnly>
      </header>

      {isLoading ? (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4" role="status" aria-label="成員資料載入中">
          {[1, 2, 3, 4].map((key) => <Skeleton key={key} className="h-64 w-full rounded-xl" />)}
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
          {members.map((member) => (
            <article key={member.id} className="min-w-0">
              <Card className="glass-panel flex h-full w-full flex-col overflow-hidden border-0 text-left">
                <div className="relative h-24 bg-[var(--secondary)]">
                  <AdminOnly>
                    <div className="absolute right-2 top-2 z-10 flex gap-2">
                      <MemberDialog member={member}>
                        <Button variant="secondary" size="icon" className="size-11" aria-label={`編輯成員：${member.name}`}>
                          <Pencil aria-hidden="true" />
                        </Button>
                      </MemberDialog>
                      <ConfirmDialog
                        trigger={
                          <Button variant="destructive" size="icon" className="size-11" aria-label={`刪除成員：${member.name}`}>
                            <Trash2 aria-hidden="true" />
                          </Button>
                        }
                        title={`刪除成員 ${member.name}？`}
                        description="成員資料刪除後無法復原，請確認後再繼續。"
                        onConfirm={async () => { await deleteMutation.mutateAsync(member.id) }}
                      />
                    </div>
                  </AdminOnly>
                  <div className="absolute -bottom-8 left-4">
                    <Avatar className="size-16 border-4 border-[var(--card)] shadow-lg">
                      <AvatarFallback className="bg-[var(--primary)] text-2xl font-bold text-[var(--primary-foreground)]">{member.name.charAt(0)}</AvatarFallback>
                    </Avatar>
                  </div>
                </div>
                <CardHeader className="pb-2 pt-10">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <CardTitle className="truncate text-lg">{member.name}</CardTitle>
                      <p className="truncate text-sm text-[var(--muted-foreground)]">{member.title}</p>
                    </div>
                    {member.bloodType && <Badge variant="secondary" className="shrink-0 text-xs">{member.bloodType}型</Badge>}
                  </div>
                </CardHeader>
                <CardContent className="space-y-3 pb-5">
                  <p className="line-clamp-3 break-words text-sm text-[var(--muted-foreground)]">{member.bio || '尚未留下個人介紹'}</p>
                  <p className="flex items-center gap-2 text-xs text-[var(--muted-foreground)]"><span>國曆生日</span><span>{member.birthday ? member.birthday.split('T')[0] : '--'}</span></p>
                  <p className="flex items-center gap-2 text-xs text-[var(--lunar)]"><span>農曆生日</span><span>{member.lunarBirthday?.displayText || '--'}</span></p>
                </CardContent>
              </Card>
            </article>
          ))}
        </div>
      )}
    </div>
  )
}
