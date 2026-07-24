'use client'

import { useState } from 'react'
import { MapPinned, Pencil, Plus, Search, Trash2, Users } from 'lucide-react'
import AdminOnly from '@/components/common/AdminOnly'
import ConfirmDialog from '@/components/common/ConfirmDialog'
import { ItineraryDialog } from '@/components/features/itineraries/ItineraryDialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { useDeleteItinerary, useItineraries } from '@/hooks/useItineraries'

export default function ItinerariesPage() {
  const [searchQuery, setSearchQuery] = useState('')
  const [debouncedQuery, setDebouncedQuery] = useState('')
  const deleteMutation = useDeleteItinerary()
  const { data, fetchNextPage, hasNextPage, isFetching, isFetchingNextPage, status } = useItineraries(debouncedQuery)

  return (
    <div className="space-y-6">
      <header className="flex flex-col items-start justify-between gap-4 lg:flex-row lg:items-center">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold tracking-tight">
            <MapPinned aria-hidden="true" className="size-6" />
            歷年行程
          </h1>
          <p className="mt-1 text-sm text-[var(--muted-foreground)]">家庭行程紀錄 — 支援標籤篩選與文字搜尋</p>
        </div>
        <div className="flex w-full flex-col gap-2 sm:flex-row lg:w-auto">
          <form onSubmit={(event) => { event.preventDefault(); setDebouncedQuery(searchQuery) }} className="flex flex-1 gap-2">
            <div className="flex-1">
              <Label htmlFor="itinerary-search" className="sr-only">搜尋行程</Label>
              <Input id="itinerary-search" type="search" autoComplete="off" maxLength={200} placeholder="搜尋行程…" className="h-11 w-full bg-[var(--secondary)] sm:w-64 glass-input" value={searchQuery} onChange={(event) => setSearchQuery(event.target.value)} />
            </div>
            <Button type="submit" variant="secondary" className="h-11 gap-2">
              <Search aria-hidden="true" />
              搜尋
            </Button>
          </form>
          <AdminOnly>
            <ItineraryDialog>
              <Button className="h-11 gap-2 whitespace-nowrap glass-button">
                <Plus aria-hidden="true" />
                新增行程
              </Button>
            </ItineraryDialog>
          </AdminOnly>
        </div>
      </header>

      <div className="space-y-3">
        {status === 'pending' ? (
          <div role="status" aria-label="行程資料載入中">
            {Array.from({ length: 3 }).map((_, index) => <Skeleton key={index} className="mb-3 h-24 w-full rounded-lg" />)}
          </div>
        ) : status === 'error' ? (
          <p className="py-12 text-center text-[var(--muted-foreground)]">無法載入行程資料</p>
        ) : data.pages[0]?.length === 0 ? (
          <p className="py-12 text-center text-[var(--muted-foreground)]">找不到相關行程</p>
        ) : (
          <>
            {data.pages.flatMap((page) => page).map((item) => {
              const dateParts = item.date.split('T')[0].split('-')
              return (
                <article key={item.id}>
                  <Card className="glass-panel relative border-0">
                    <CardContent className="flex min-h-24 items-center gap-4 py-4 pr-28">
                      <div className="flex size-14 shrink-0 flex-col items-center justify-center rounded-lg bg-[var(--secondary)]">
                        <span className="text-xs text-[var(--muted-foreground)]">{dateParts[0]}</span>
                        <span className="text-lg font-bold">{dateParts[1]}/{dateParts[2]}</span>
                      </div>
                      <div className="min-w-0 flex-1">
                        <h2 className="truncate font-medium">{item.title}</h2>
                        <p className="flex items-center gap-1 truncate text-sm text-[var(--muted-foreground)]">
                          {item.location && <><MapPinned aria-hidden="true" className="size-4" />{item.location}<span aria-hidden="true">·</span></>}
                          <Users aria-hidden="true" className="size-4" />{item.participants?.length || 0} 人
                        </p>
                      </div>
                      <div className="hidden shrink-0 gap-1.5 md:flex">{item.tags.map((tag) => <Badge key={tag} variant="secondary" className="text-xs">{tag}</Badge>)}</div>
                      <AdminOnly>
                        <div className="absolute right-2 top-1/2 flex -translate-y-1/2 gap-2">
                          <ItineraryDialog itinerary={item}>
                            <Button variant="secondary" size="icon" className="size-11" aria-label={`編輯行程：${item.title}`}><Pencil aria-hidden="true" /></Button>
                          </ItineraryDialog>
                          <ConfirmDialog
                            trigger={<Button variant="destructive" size="icon" className="size-11" aria-label={`刪除行程：${item.title}`}><Trash2 aria-hidden="true" /></Button>}
                            title={`刪除行程 ${item.title}？`}
                            description="行程紀錄刪除後無法復原，請確認後再繼續。"
                            onConfirm={async () => { await deleteMutation.mutateAsync(item.id) }}
                          />
                        </div>
                      </AdminOnly>
                    </CardContent>
                  </Card>
                </article>
              )
            })}
            {hasNextPage && <div className="pt-4 text-center"><Button variant="outline" className="h-11" onClick={() => fetchNextPage()} disabled={isFetchingNextPage}>{isFetchingNextPage ? '載入中…' : '載入更多'}</Button></div>}
            {isFetching && !isFetchingNextPage && <div className="mt-2 text-center text-xs text-muted-foreground" role="status">更新資料中…</div>}
          </>
        )}
      </div>
    </div>
  )
}
