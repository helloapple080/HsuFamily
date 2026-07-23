'use client'

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import type { CalendarEvent, UpsertEventRequest } from '@/types/event'
import type { ApiResponse } from '@/types/api'
import api from '@/lib/api'
import { toast } from 'sonner'
import axios from 'axios'

export function useCalendarEvents(start: string, end: string) {
  return useQuery({
    queryKey: ['events', start, end],
    queryFn: async () => {
      const res = await api.get<ApiResponse<CalendarEvent[]>>('/api/events', {
        params: { start, end },
      })
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data ?? []
    },
  })
}

export function useCreateEvent() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (data: UpsertEventRequest) => {
      const res = await api.post<ApiResponse<CalendarEvent>>('/api/events', data)
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['events'] })
      toast.success('事件已建立')
    },
    onError: (error) => {
      if (axios.isAxiosError(error)) {
        toast.error(error.response?.data?.message || '建立失敗')
      } else {
        toast.error((error as Error).message || '建立失敗')
      }
    },
  })
}

export function useUpdateEvent() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpsertEventRequest }) => {
      const res = await api.put<ApiResponse<CalendarEvent>>(`/api/events/${id}`, data)
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['events'] })
      toast.success('事件已更新')
    },
    onError: (error) => {
      if (axios.isAxiosError(error)) {
        toast.error(error.response?.data?.message || '更新失敗')
      } else {
        toast.error((error as Error).message || '更新失敗')
      }
    },
  })
}

export function useDeleteEvent() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await api.delete<ApiResponse<boolean>>(`/api/events/${id}`)
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['events'] })
      toast.success('事件已刪除')
    },
    onError: (error) => {
      if (axios.isAxiosError(error)) {
        toast.error(error.response?.data?.message || '刪除失敗')
      } else {
        toast.error((error as Error).message || '刪除失敗')
      }
    },
  })
}
