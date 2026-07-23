'use client'

import { motion } from 'framer-motion'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'
import { useMembers, useDeleteMember } from '@/hooks/useMembers'
import { MemberDialog } from '@/components/features/members/MemberDialog'
import { Button } from '@/components/ui/button'

export default function MembersPage() {
  const { data: members = [], isLoading } = useMembers()
  const deleteMutation = useDeleteMember()

  const handleDelete = async (e: React.MouseEvent, id: string, name: string) => {
    e.stopPropagation()
    if (confirm(`確定要刪除成員 ${name} 嗎？`)) {
      await deleteMutation.mutateAsync(id)
    }
  }

  return (
    <div className="space-y-6">
      <motion.div
        initial={{ opacity: 0, y: -10 }}
        animate={{ opacity: 1, y: 0 }}
        className="flex items-center justify-between"
      >
        <div>
          <h1 className="text-2xl font-bold tracking-tight">👨‍👩‍👧‍👦 家庭成員</h1>
          <p className="text-sm text-[var(--muted-foreground)] mt-1">
            家庭成員資料管理 — 包含農曆生日
          </p>
        </div>
        
        <MemberDialog>
          <Button className="glass-button shadow-md">+ 新增成員</Button>
        </MemberDialog>
      </motion.div>

      {isLoading ? (
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-4">
          {[1, 2, 3, 4].map(key => (
            <Skeleton key={key} className="h-64 w-full rounded-xl" />
          ))}
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-4">
          {members.map((member, index) => (
            <motion.div
              key={member.id}
              initial={{ opacity: 0, y: 30 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: index * 0.1 }}
              whileHover={{ y: -4, scale: 1.02 }}
            >
              <MemberDialog member={member}>
                <Card className="glass-panel border-0 overflow-hidden group cursor-pointer text-left w-full h-full flex flex-col hover:shadow-xl transition-all duration-300">
                  <div className="h-24 bg-gradient-to-br from-[oklch(0.5_0.15_260/0.3)] to-[oklch(0.4_0.12_310/0.2)] relative">
                  <div className="absolute inset-0 bg-gradient-to-t from-[var(--card)] to-transparent" />
                  <div className="absolute top-3 right-3 opacity-0 group-hover:opacity-100 transition-opacity flex gap-2">
                    <Button 
                      variant="destructive" 
                      size="sm" 
                      className="h-7 px-2 bg-red-500/80 hover:bg-red-500 text-xs backdrop-blur-md"
                      onClick={(e) => handleDelete(e, member.id, member.name)}
                    >
                      刪除
                    </Button>
                  </div>
                  <div className="absolute -bottom-8 left-4">
                    <Avatar className="h-16 w-16 border-4 border-[var(--card)] shadow-lg">
                      <AvatarFallback className="text-2xl bg-gradient-to-br from-[oklch(0.6_0.18_260)] to-[oklch(0.5_0.15_310)] font-bold text-white">
                        {member.name.charAt(0)}
                      </AvatarFallback>
                    </Avatar>
                  </div>
                </div>

                <CardHeader className="pt-10 pb-2">
                  <div className="flex items-start justify-between">
                    <div>
                      <CardTitle className="text-lg">{member.name}</CardTitle>
                      <p className="text-sm text-[var(--muted-foreground)]">{member.title}</p>
                    </div>
                    {member.bloodType && (
                      <Badge variant="secondary" className="text-xs">
                        {member.bloodType}型
                      </Badge>
                    )}
                  </div>
                </CardHeader>

                <CardContent className="space-y-3 pb-5">
                  <p className="text-sm text-[var(--muted-foreground)]">
                    {member.bio || '尚未留下個人介紹'}
                  </p>

                  <div className="flex items-center gap-2 text-xs text-[var(--muted-foreground)]">
                    <span>🎂 國曆生日</span>
                    <span>{member.birthday ? member.birthday.split('T')[0] : '--'}</span>
                  </div>
                  <div className="flex items-center gap-2 text-xs text-[var(--lunar)]">
                    <span>🌙 農曆生日</span>
                    <span>{member.lunarBirthday?.displayText || '--'}</span>
                  </div>
                </CardContent>
                </Card>
              </MemberDialog>
            </motion.div>
          ))}
        </div>
      )}
    </div>
  )
}
