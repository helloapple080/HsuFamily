'use client'

import { useState } from 'react'
import { motion } from 'framer-motion'
import { Card, CardContent } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useItineraries, useDeleteItinerary } from '@/hooks/useItineraries'
import { ItineraryDialog } from '@/components/features/itineraries/ItineraryDialog'

export default function ItinerariesPage() {
  const [searchQuery, setSearchQuery] = useState('')
  const [debouncedQuery, setDebouncedQuery] = useState('')
  const deleteMutation = useDeleteItinerary()

  const handleDelete = async (e: React.MouseEvent, id: string, title: string) => {
    e.stopPropagation()
    if (confirm(`確定要刪除行程 ${title} 嗎？`)) {
      await deleteMutation.mutateAsync(id)
    }
  }

  const {
    data,
    fetchNextPage,
    hasNextPage,
    isFetching,
    isFetchingNextPage,
    status,
  } = useItineraries(debouncedQuery)

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault()
    setDebouncedQuery(searchQuery)
  }

  return (
    <div className="space-y-6">
      <motion.div
        initial={{ opacity: 0, y: -10 }}
        animate={{ opacity: 1, y: 0 }}
        className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4"
      >
        <div>
          <h1 className="text-2xl font-bold tracking-tight">📜 歷年行程</h1>
          <p className="text-sm text-[var(--muted-foreground)] mt-1">
            家庭行程紀錄 — 支援標籤篩選與文字搜尋
          </p>
        </div>

        <div className="flex gap-2 w-full sm:w-auto items-center">
          <form onSubmit={handleSearch} className="flex gap-2 flex-1">
            <Input 
              type="search" 
              placeholder="搜尋行程..." 
              className="w-full sm:w-64 bg-[var(--secondary)] glass-input"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
            <Button type="submit" variant="secondary">搜尋</Button>
          </form>
          <ItineraryDialog>
            <Button className="glass-button whitespace-nowrap">+ 新增行程</Button>
          </ItineraryDialog>
        </div>
      </motion.div>

      {/* 行程列表 */}
      <div className="space-y-3">
        {status === 'pending' ? (
          Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-24 w-full rounded-lg" />
          ))
        ) : status === 'error' ? (
          <div className="py-12 text-center text-[var(--muted-foreground)]">無法載入行程資料</div>
        ) : data.pages[0]?.length === 0 ? (
          <div className="py-12 text-center text-[var(--muted-foreground)]">找不到相關行程</div>
        ) : (
          <>
            {data.pages.map((page, i) => (
              <div key={i} className="space-y-3">
                {page.map((item, index) => {
                  const dateParts = item.date.split('T')[0].split('-')
                  return (
                    <motion.div
                      key={item.id}
                      initial={{ opacity: 0, x: -20 }}
                      animate={{ opacity: 1, x: 0 }}
                      transition={{ delay: index * 0.05 }}
                      whileHover={{ x: 4 }}
                    >
                      <ItineraryDialog itinerary={item}>
                        <Card className="glass-panel border-0 hover:shadow-lg transition-all duration-300 cursor-pointer relative group">
                          <CardContent className="flex items-center gap-4 py-4">
                            {/* 日期標記 */}
                            <div className="w-14 h-14 rounded-lg bg-gradient-to-br from-[oklch(0.5_0.15_260/0.2)] to-[oklch(0.4_0.12_310/0.15)] flex flex-col items-center justify-center shrink-0">
                              <span className="text-xs text-[var(--muted-foreground)]">
                                {dateParts[0]}
                              </span>
                              <span className="text-lg font-bold">
                                {dateParts[1]}/{dateParts[2]}
                              </span>
                            </div>

                            {/* 內容 */}
                            <div className="flex-1 min-w-0">
                              <h3 className="font-medium truncate">{item.title}</h3>
                              <p className="text-sm text-[var(--muted-foreground)] truncate">
                                {item.location && `📍 ${item.location} · `}👥 {item.participants?.length || 0} 人
                              </p>
                            </div>

                            {/* 標籤 */}
                            <div className="flex gap-1.5 shrink-0 hidden sm:flex">
                              {item.tags.map((tag) => (
                                <Badge key={tag} variant="secondary" className="text-xs">
                                  {tag}
                                </Badge>
                              ))}
                            </div>

                            {/* 刪除按鈕 (Hover時顯示) */}
                            <Button
                              variant="destructive"
                              size="sm"
                              className="absolute top-2 right-2 h-7 px-2 bg-red-500/80 hover:bg-red-500 text-xs backdrop-blur-md opacity-0 group-hover:opacity-100 transition-opacity"
                              onClick={(e) => handleDelete(e, item.id, item.title)}
                            >
                              刪除
                            </Button>
                          </CardContent>
                        </Card>
                      </ItineraryDialog>
                    </motion.div>
                  )
                })}
              </div>
            ))}

            {hasNextPage && (
              <div className="pt-4 text-center">
                <Button 
                  variant="outline" 
                  onClick={() => fetchNextPage()} 
                  disabled={isFetchingNextPage}
                >
                  {isFetchingNextPage ? '載入中...' : '載入更多'}
                </Button>
              </div>
            )}
            
            {/* Background fetching indicator */}
            {isFetching && !isFetchingNextPage && (
               <div className="text-center text-xs text-muted-foreground mt-2">更新資料中...</div>
            )}
          </>
        )}
      </div>
    </div>
  )
}
