'use client'

import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import type { Itinerary, UpsertItineraryRequest } from '@/types/event'
import type { ApiResponse } from '@/types/api'
import api from '@/lib/api'
import { toast } from 'sonner'

interface ItineraryPageParam {
  cursorDate?: string
  cursorId?: string
}

export function useItineraries(query?: string) {
  return useInfiniteQuery({
    queryKey: ['itineraries', query],
    queryFn: async ({ pageParam }) => {
      if (query?.trim()) {
        const res = await api.get<ApiResponse<Itinerary[]>>('/api/itineraries/search', {
          params: { keyword: query.trim(), limit: 50 },
        })
        if (!res.data.success) throw new Error(res.data.message)
        return res.data.data ?? []
      }

      const { cursorDate, cursorId } = pageParam
      const res = await api.get<ApiResponse<Itinerary[]>>('/api/itineraries', {
        params: {
          query,
          cursorDate,
          cursorId,
          pageSize: 20,
        },
      })
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data ?? []
    },
    initialPageParam: {} as ItineraryPageParam,
    getNextPageParam: (lastPage) => {
      if (query?.trim()) return undefined
      if (lastPage.length < 20) return undefined
      const lastItem = lastPage[lastPage.length - 1]
      return {
        cursorDate: lastItem.date,
        cursorId: lastItem.id,
      }
    },
  })
}

// Create/Update/Delete mutations
export function useCreateItinerary() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (data: UpsertItineraryRequest) => {
      const res = await api.post<ApiResponse<Itinerary>>('/api/itineraries', data)
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data
    },
    onSuccess: () => {
      toast.success('新增行程成功')
      queryClient.invalidateQueries({ queryKey: ['itineraries'] })
    },
    onError: (err: Error) => toast.error('新增失敗', { description: err.message }),
  })
}

export function useUpdateItinerary() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpsertItineraryRequest }) => {
      const res = await api.put<ApiResponse<Itinerary>>(`/api/itineraries/${id}`, data)
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data
    },
    onSuccess: () => {
      toast.success('更新行程成功')
      queryClient.invalidateQueries({ queryKey: ['itineraries'] })
    },
    onError: (err: Error) => toast.error('更新失敗', { description: err.message }),
  })
}

export function useDeleteItinerary() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const res = await api.delete<ApiResponse<null>>(`/api/itineraries/${id}`)
      if (!res.data.success) throw new Error(res.data.message)
      return id
    },
    onSuccess: () => {
      toast.success('已刪除行程')
      queryClient.invalidateQueries({ queryKey: ['itineraries'] })
    },
    onError: (err: Error) => toast.error('刪除失敗', { description: err.message }),
  })
}
